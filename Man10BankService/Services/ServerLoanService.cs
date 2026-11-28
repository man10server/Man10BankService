using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public partial class ServerLoanService
{
    // ===== 設定値(appsettings.json の ServerLoan セクション。Configure で読み込む) =====

    // 日利(複利)。毎日「残債 × 日利」を残債に加える。
    private static decimal DailyInterestRate { get; set; } = 0.02m;
    // 借入枠の下限(初期枠)
    private static decimal MinAmount { get; set; } = 100000m;
    // 借入枠の上限
    private static decimal MaxAmount { get; set; } = 3000000m;
    // 利息で増える残債(元本 + 利息)の上限。0 以下なら上限なし。
    private static decimal MaxDebtAmount { get; set; } = 10000000m;
    // 定期返済の成功時に、「返済額」と「その週の平均残債」の小さいほうへ掛けて枠に加える係数
    private static decimal GrowthFactor { get; set; } = 0.5m;
    // 1回の増枠の上限(その時点の枠に対する割合)
    private static decimal MaxGrowthRate { get; set; } = 0.1m;
    // 定期返済の失敗時に枠へ掛ける率
    private static decimal FailureCutRate { get; set; } = 0.5m;
    // 最低返済額の割合(残債に対して)
    private static decimal MinPaymentRate { get; set; } = 0.02m;
    // 最低返済額の固定下限
    private static decimal MinPaymentAmount { get; set; } = 10000m;
    // 最低返済額で、1週間分の利息に上乗せして返す元本の割合(残債に対して)。0 以下なら利息を見ない。
    private static decimal MinPrincipalRate { get; set; } = 0.01m;
    // 参加者がいる契約(連携契約)の借入枠の上限。未設定なら MaxAmount と同じ。
    private static decimal? LinkedMaxAmount { get; set; }
    // 参加者がいる契約(連携契約)の日利。未設定なら DailyInterestRate と同じ。
    private static decimal? LinkedDailyInterestRate { get; set; }
    // 日次処理(利息の加算・延滞分の回収)の時刻(UTC)
    private static TimeSpan DailyInterestTime { get; set; } = new(2, 0, 0); // 02:00
    // 定期返済の時刻(UTC)と曜日
    private static TimeSpan WeeklyRepayTime { get; set; } = new(3, 0, 0);   // 03:00
    private static DayOfWeek WeeklyRepayDay { get; set; } = DayOfWeek.Monday;

    // ===== 固定値 =====

    // 定期返済の間隔(日数)。増枠の平均残債と、最低返済額に含める利息の算定に使う。
    private const int RepayIntervalDays = 7;
    // 支払額(設定)の初期値を、利息の何週間分にするか。
    private const int InitialPaymentWeeks = 2;

    // 借入枠の算定で参照するログの種類(server_loan_log.action の値)
    private const string RepaySuccessAction = nameof(ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
    private const string RepayFailureAction = nameof(ServerLoanRepository.ServerLoanLogAction.RepayFailure);
    private const string InterestAction = nameof(ServerLoanRepository.ServerLoanLogAction.Interest);
    private const string MemberJoinAction = nameof(ServerLoanRepository.ServerLoanLogAction.MemberJoin);
    private const string MemberLeaveAction = nameof(ServerLoanRepository.ServerLoanLogAction.MemberLeave);
    private const string LimitResetAction = nameof(ServerLoanRepository.ServerLoanLogAction.LimitReset);

    // スケジューラ(BackgroundService)が参照する実行時刻設定
    public static TimeSpan DailyInterestTimeOfDay => DailyInterestTime;
    public static TimeSpan WeeklyRepayTimeOfDay => WeeklyRepayTime;
    public static DayOfWeek WeeklyRepayDayOfWeek => WeeklyRepayDay;

    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bank;
    private readonly IPlayerProfileService _profileService;
    private readonly ILogger<ServerLoanService>? _logger;

    public ServerLoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService, IConfiguration config,
        ILogger<ServerLoanService>? logger = null)
    {
        Configure(config);

        // 最低返済額に利息を含めない設定(MinPrincipalRate が 0 以下)で、率が週あたりの金利以下だと残債が減らない。
        var weeklyInterestRate = DailyInterestRate * RepayIntervalDays;
        if (MinPrincipalRate <= 0m && MinPaymentRate <= weeklyInterestRate)
        {
            logger?.LogWarning(
                "ServerLoan:MinPaymentRate({MinPaymentRate}) が週あたりの金利({WeeklyInterestRate})以下のため、最低返済額では残債が減りません。",
                MinPaymentRate, weeklyInterestRate);
        }

        _dbFactory = dbFactory;
        _bank = bank;
        _profileService = profileService;
        _logger = logger;
    }

    // uuid が使う契約(server_loan_tbl の行)を返す。他のプレイヤーの契約に参加しているアカウントには、参加先の契約を返す。
    // 契約の行がまだ無いプレイヤーには、残債 0 の行を作って返す。
    public Task<ApiResult<ServerLoan>> GetByUuidAsync(string uuid)
    {
        return ReadAsync(async repo =>
        {
            if (await _profileService.GetNameByUuidAsync(uuid) == null)
                return ApiResult<ServerLoan>.Fail(ErrorCode.PlayerNotFound);

            var contractUuid = await repo.ResolveContractUuidAsync(uuid);
            return ApiResult<ServerLoan>.Ok(await repo.GetOrCreateByUuidAsync(contractUuid));
        });
    }

    // /mrevo に表示する返済情報: 次回の定期返済の日時、1 日あたりの利息、次回の定期返済で引き落とす額。
    // どれも現時点の残債で計算する。返済日までに付く利息の分だけ、実際の引き落とし額は増えることがある。
    public async Task<ApiResult<PaymentInfoResponse>> GetPaymentInfoAsync(string uuid)
    {
        var loanRes = await GetByUuidAsync(uuid);
        if (!loanRes.IsSuccess)
            return ApiResult<PaymentInfoResponse>.Fail(loanRes.Code);
        var loan = loanRes.Data!;

        return await ReadAsync(async repo =>
        {
            var linked = await repo.HasMembersAsync(loan.Uuid);
            var interestPerDay = loan.StopInterest ? 0m : CalculateDailyInterestAmount(loan.BorrowAmount, linked);
            var nextPayment = CalculateScheduledPayment(loan.BorrowAmount, loan.PaymentAmount, loan.StopInterest, linked);
            return ApiResult<PaymentInfoResponse>.Ok(new PaymentInfoResponse(GetNextWeeklyRepayDateTime(), interestPerDay, nextPayment));
        });
    }

    // uuid が使う契約のログ(新しい順)。
    public async Task<ApiResult<List<ServerLoanLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<ServerLoanLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<ServerLoanLog>>.Fail(ErrorCode.OffsetOutOfRange);

        return await ReadAsync(async repo =>
        {
            var contractUuid = await repo.ResolveContractUuidAsync(uuid);
            return ApiResult<List<ServerLoanLog>>.Ok(await repo.GetLogsAsync(contractUuid, limit, offset));
        });
    }

    // uuid が使う契約の借入枠。
    public Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid)
    {
        return ReadAsync(async repo =>
        {
            var contractUuid = await repo.ResolveContractUuidAsync(uuid);
            var (debt, logs) = await repo.GetBorrowLimitSourceAsync(contractUuid);
            return ApiResult<decimal>.Ok(CalculateBorrowLimit(logs, debt));
        });
    }

    // uuid が使う契約に残債があり延滞中(直近の定期返済が失敗のまま)かどうか。ローンが無いプレイヤーは false で、行は作らない。
    public Task<ApiResult<bool>> HasOverdueDebtAsync(string uuid)
    {
        return ReadAsync(async _ =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return ApiResult<bool>.Ok(await ServerLoanRepository.HasOverdueDebtAsync(db, uuid));
        });
    }

    // 読み取りだけの処理の共通部分。DB の例外は UnexpectedError に変換する。
    // (書き込みは BankService.RunExclusiveAsync を通し、そちらが例外を変換する。)
    private async Task<ApiResult<T>> ReadAsync<T>(Func<ServerLoanRepository, Task<ApiResult<T>>> read)
    {
        try
        {
            return await read(new ServerLoanRepository(_dbFactory, _profileService));
        }
        catch (Exception)
        {
            return ApiResult<T>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 借入: uuid が使う契約(参加先があればその本垢の契約)の残債を増やし、借りたお金を uuid の口座に入れる。
    // 判定から記録までを 1 トランザクションで行う(契約の行ロック → 延滞と借入枠の判定 → 残債の加算とログ → 口座への入金)。
    // ロック順序規約: 自リソース行(server_loan)→user_bank 行。
    public async Task<ApiResult<ServerLoan>> BorrowAsync(string uuid, decimal amount)
    {
        if (amount <= 0m)
            return ApiResult<ServerLoan>.Fail(ErrorCode.ValidationError);

        // 名前解決(ログイン時登録の名簿 → 口座の名前)はワーカー外で行う。
        var resolved = await _bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<ServerLoan>.Fail(resolved.Code);
        var resolvedPlayer = resolved.Data!;

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan>(async db =>
        {
            var contractUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, uuid);

            // 1) server_loan 行ロック(MySQL 時 FOR UPDATE)
            var loan = await repo.GetOrCreateForUpdateAsync(db, contractUuid);

            // 2) 延滞中(直近の定期返済が失敗のまま)は、次の定期返済が成功するまで借りられない。
            if (await ServerLoanRepository.IsLatestScheduledRepayFailureAsync(db, contractUuid))
                return ApiResult<ServerLoan>.Fail(ErrorCode.RepaymentOverdue);

            // 3) 借入枠の判定(枠は契約のログから毎回計算する)
            var limit = CalculateBorrowLimit(await ServerLoanRepository.GetLogsOldestFirstAsync(db, contractUuid), loan.BorrowAmount);
            if (loan.BorrowAmount + amount > limit)
                return ApiResult<ServerLoan>.Fail(ErrorCode.BorrowLimitExceeded);

            // 本垢自身が借りるときは、契約の行に持つ名前を最新の名前にしておく(改名への追従)。
            if (contractUuid == uuid)
                loan.Player = resolvedPlayer;

            // 支払額が未設定の契約は、この借入額をもとに初期値を設定する(残債を増やす前に判定しておく)。
            var needPayment = loan.PaymentAmount <= 0m;

            // 4) 債務加算 + ServerLoanLog
            ServerLoanRepository.AdjustLoanCore(db, loan, amount, ServerLoanRepository.ServerLoanLogAction.Borrow, actorUuid: uuid);

            if (needPayment)
                loan.PaymentAmount = CalculateInitialPaymentAmount(amount, await ServerLoanRepository.HasMembersAsync(db, contractUuid));

            // 5) 借りたお金は、操作したアカウントの口座に入れる。user_bank 残高加算 + MoneyLog(同一 tx)
            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, resolvedPlayer, amount,
                "server_loan", "loan_borrow", "サーバーローン借入", "system");

            return ApiResult<ServerLoan>.Ok(loan);
        });
    }

    // 手動返済: プレイヤーが自分の口座から返す。uuid が使う契約(参加先があればその本垢の契約)の残債を減らす。
    // 借入枠の評価対象は定期返済だけなので、EarlyRepay として記録する。残高不足でも延滞にはならない。
    // ロック順序規約: 自リソース行(server_loan)→user_bank 行。
    public async Task<ApiResult<ServerLoan>> RepayAsync(string uuid, decimal amount)
    {
        if (amount <= 0m)
            return ApiResult<ServerLoan>.Fail(ErrorCode.ValidationError);

        var resolved = await _bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<ServerLoan>.Fail(resolved.Code);
        var resolvedPlayer = resolved.Data!;

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan>(async db =>
        {
            var contractUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, uuid);

            // 1) server_loan 行ロック
            var loan = await repo.GetOrCreateForUpdateAsync(db, contractUuid);
            if (loan.BorrowAmount <= 0m)
                return ApiResult<ServerLoan>.Fail(ErrorCode.NoRepaymentNeeded);

            // 残債を超える分は返済しない。
            var pay = Math.Min(amount, loan.BorrowAmount);

            // 2) 操作したアカウントの口座を行ロックし、残高不足を判定
            var account = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            if ((account?.Balance ?? 0m) < pay)
                return ApiResult<ServerLoan>.Fail(ErrorCode.InsufficientFunds);

            // 3) 残高減算 + MoneyLog
            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, resolvedPlayer, -pay,
                "server_loan", "loan_repay", "サーバーローン返済", "system");

            // 4) 債務減算 + 返済ログ
            ServerLoanRepository.AdjustLoanCore(db, loan, -pay, ServerLoanRepository.ServerLoanLogAction.EarlyRepay, actorUuid: uuid);

            return ApiResult<ServerLoan>.Ok(loan);
        });
    }

    // 定期返済(契約 1 件分): 支払額(設定)と最低返済額の大きいほうを、契約の全アカウントの口座から引き落とす。
    //  - 口座は本垢→参加が古い順に使う。全員の残高の合計で足りなければ、どの口座からも引き落とさず失敗とする。
    //  - 成功は RepaySuccess、失敗は RepayFailure として記録する。この 2 つが借入枠の増減と延滞の判定に使われる。
    //  - 延滞後に手動返済で完済したまま返済日を迎えた契約は、0 円の成功を記録して延滞を解消する。
    // 戻り値は、ログを記録したかどうか。
    private async Task<ApiResult<bool>> RepayScheduledAsync(string ownerUuid)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            // 1) server_loan 行ロック
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null)
                return ApiResult<bool>.Ok(false);

            var debt = loan.BorrowAmount;
            if (debt <= 0m)
            {
                if (!await ServerLoanRepository.IsLatestScheduledRepayFailureAsync(db, ownerUuid))
                    return ApiResult<bool>.Ok(false);

                ServerLoanRepository.AdjustLoanCore(db, loan, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                return ApiResult<bool>.Ok(true);
            }

            var amount = CalculateScheduledPayment(debt, loan.PaymentAmount, loan.StopInterest, await ServerLoanRepository.HasMembersAsync(db, ownerUuid));
            // 最低返済額の設定をすべて 0 にしていて支払額も未設定のときだけ 0 になる。引き落とすものが無いので何もしない。
            if (amount <= 0m)
                return ApiResult<bool>.Ok(false);

            // 2) 契約の全アカウントの口座を行ロックし、合計残高で残高不足を判定
            var accounts = await LockBankAccountsAsync(db, await ServerLoanRepository.GetContractAccountUuidsAsync(db, ownerUuid));
            if (TotalBalance(accounts) < amount)
            {
                // 残債は変えず、引き落とそうとした額を失敗として記録する(これで延滞になる)。
                ServerLoanRepository.AdjustLoanCore(db, loan, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
                return ApiResult<bool>.Ok(true);
            }

            // 3) 残高減算 + MoneyLog
            await WithdrawFromAccountsAsync(db, accounts, amount, "loan_repay", "サーバーローン返済");

            // 4) 債務減算 + 返済ログ
            ServerLoanRepository.AdjustLoanCore(db, loan, -amount, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
            return ApiResult<bool>.Ok(true);
        });
    }

    // 延滞分の回収: 直近の定期返済が失敗のままの契約について、未回収分を銀行残高のある分だけ回収する。
    // 契約の全アカウント(本垢→参加が古い順)の口座から回収する。
    // 失敗額を回収しきるか残債が 0 になったら、0 円の成功ログを残して延滞を解消する(借入枠は増えない)。
    // 戻り値は、ログを記録したかどうか。
    private async Task<ApiResult<bool>> CollectOverdueAsync(string ownerUuid)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null || loan.BorrowAmount <= 0m)
                return ApiResult<bool>.Ok(false);

            // 延滞中でなければ(直近の定期返済が失敗でなければ)回収するものは無い。
            var uncollected = await ServerLoanRepository.GetUncollectedOverdueAmountAsync(db, ownerUuid);
            if (uncollected is null)
                return ApiResult<bool>.Ok(false);

            // 回収したい額(target)のうち、全員の口座にある分(amount)だけ回収する。
            var accounts = await LockBankAccountsAsync(db, await ServerLoanRepository.GetContractAccountUuidsAsync(db, ownerUuid));
            var target = Math.Min(uncollected.Value, loan.BorrowAmount);
            var amount = Math.Min(target, TotalBalance(accounts));
            if (target > 0m && amount <= 0m)
                return ApiResult<bool>.Ok(false);

            if (amount > 0m)
            {
                await WithdrawFromAccountsAsync(db, accounts, amount, "loan_overdue_collect", "サーバーローン延滞分の回収");
                ServerLoanRepository.AdjustLoanCore(db, loan, -amount, ServerLoanRepository.ServerLoanLogAction.OverdueCollect);
            }

            // 回収しきったら延滞を解消する。
            if (amount >= target)
                ServerLoanRepository.AdjustLoanCore(db, loan, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);

            return ApiResult<bool>.Ok(true);
        });
    }

    // 口座を順に行ロックして読む(MySQL 時 FOR UPDATE)。口座が無いアカウントは含めない。
    private static async Task<List<UserBank>> LockBankAccountsAsync(BankDbContext db, IEnumerable<string> uuids)
    {
        var accounts = new List<UserBank>();
        foreach (var uuid in uuids)
        {
            var account = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            if (account != null)
                accounts.Add(account);
        }

        return accounts;
    }

    private static decimal TotalBalance(IEnumerable<UserBank> accounts)
    {
        return accounts.Sum(x => Math.Max(0m, x.Balance));
    }

    // 先頭の口座から順に、合計が amount になるまで引き落とす(残高 + MoneyLog。SaveChanges は呼び出し側)。
    // 呼び出し前に、口座の合計残高が amount 以上であることを確かめておくこと。
    private async Task WithdrawFromAccountsAsync(
        BankDbContext db, IEnumerable<UserBank> accounts, decimal amount, string note, string displayNote)
    {
        var remaining = amount;
        foreach (var account in accounts)
        {
            if (remaining <= 0m)
                break;

            var withdraw = Math.Min(account.Balance, remaining);
            if (withdraw <= 0m)
                continue;

            // 入出金ログには名簿の最新の名前を残す。名簿に無ければ口座の名前を使う。
            var player = await _profileService.GetNameByUuidAsync(account.Uuid) ?? account.Player;
            await BankRepository.ChangeBalanceCoreAsync(
                db, account.Uuid, player, -withdraw,
                "server_loan", note, displayNote, "system");
            remaining -= withdraw;
        }
    }

    // 借入枠を、プレイヤーの全ログ(古い順)をたどって求める。枠は DB に保存せず、毎回ここで計算する。
    //  - MinAmount から始める。
    //  - 定期返済の成功: CalculateGrowth の額だけ増やす。
    //  - 定期返済の失敗: FailureCutRate を掛けて減らす。
    //  - 参加・脱退: 参加者がいる間だけ枠の上限が LinkedMaxAmount に切り替わる。
    //  - 枠の初期化(運営による強制リンク時の罰則): MinAmount に戻す。以後の成功でまた増える。
    //  - 手動返済・延滞分の回収・借入などは枠に影響しない。
    private static decimal CalculateBorrowLimit(IReadOnlyList<ServerLoanLog> logs, decimal currentDebt)
    {
        var debtBeforeLog = CalculateDebtBeforeEachLog(logs, currentDebt);

        // その時点で契約に参加者がいたかどうか(参加・脱退のログから数える)。枠の上限の切り替えに使う。
        var members = 0;
        var limit = ClampBorrowLimit(MinAmount, linked: false);
        // 前回の定期返済より後に、利息が付いた時点の残債(平均残債の材料)
        var interestDebts = new List<(DateTime Date, decimal Debt)>();

        for (var i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            switch (log.Action)
            {
                case InterestAction:
                    interestDebts.Add((log.Date, Math.Max(0m, debtBeforeLog[i])));
                    break;

                case RepayFailureAction:
                    limit = ClampBorrowLimit(limit * FailureCutRate, members > 0);
                    interestDebts.Clear();
                    break;

                // 参加者の有無で枠の上限が変わるので、その時点の上限に収め直す。
                case MemberJoinAction:
                    members++;
                    limit = ClampBorrowLimit(limit, members > 0);
                    break;

                case MemberLeaveAction:
                    members = Math.Max(0, members - 1);
                    limit = ClampBorrowLimit(limit, members > 0);
                    break;

                case LimitResetAction:
                    limit = ClampBorrowLimit(MinAmount, members > 0);
                    break;

                // 0 円の成功は延滞解消の記録なので、枠を増やさず、その週の利息の記録も消費しない。
                case RepaySuccessAction when log.Amount != 0m:
                    // 返済ログの金額は残債の増減(負数)で記録されるため、絶対値が返済額になる。
                    var paid = Math.Abs(log.Amount);
                    var averageDebt = CalculateAverageDebt(interestDebts, log.Date);
                    limit = ClampBorrowLimit(limit + CalculateGrowth(limit, paid, averageDebt), members > 0);
                    interestDebts.Clear();
                    break;
            }
        }

        return limit;
    }

    // 各ログが記録される直前の残債を、現在の残債からログの増減を新しい順に巻き戻して求める。
    private static decimal[] CalculateDebtBeforeEachLog(IReadOnlyList<ServerLoanLog> logs, decimal currentDebt)
    {
        var debtBeforeLog = new decimal[logs.Count];
        var debt = currentDebt;
        for (var i = logs.Count - 1; i >= 0; i--)
        {
            // 返済失敗ログの金額は引き落とそうとした額で、残債は変わっていない。
            if (logs[i].Action != RepayFailureAction)
                debt -= logs[i].Amount;
            debtBeforeLog[i] = debt;
        }

        return debtBeforeLog;
    }

    // その週の平均残債: 定期返済の直近 RepayIntervalDays 日以内に利息が付いた時点の残債の合計 ÷ RepayIntervalDays。
    // 利息が付かなかった日は 0 として数えるため、返済の直前だけ借りても平均残債は増えない。
    private static decimal CalculateAverageDebt(IEnumerable<(DateTime Date, decimal Debt)> interestDebts, DateTime repayDate)
    {
        var from = repayDate.AddDays(-RepayIntervalDays);
        return interestDebts.Where(x => x.Date > from).Sum(x => x.Debt) / RepayIntervalDays;
    }

    // 定期返済の成功 1 回で増える枠: 「返済額」と「その週の平均残債」の小さいほう × GrowthFactor。
    // ただし、その時点の枠 × MaxGrowthRate を上限とする。
    private static decimal CalculateGrowth(decimal limit, decimal paid, decimal averageDebt)
    {
        var growth = Math.Min(paid, averageDebt) * GrowthFactor;
        var maxGrowth = limit * MaxGrowthRate;
        return Math.Min(growth, maxGrowth);
    }

    // 借入枠を MinAmount 〜 上限の範囲に収め、1 円未満を切り捨てる。
    // 上限は、参加者がいる契約(linked)なら LinkedMaxAmount(未設定なら MaxAmount)、いなければ MaxAmount。
    private static decimal ClampBorrowLimit(decimal limit, bool linked)
    {
        var maxAmount = linked ? LinkedMaxAmount ?? MaxAmount : MaxAmount;
        limit = Math.Max(MinAmount, limit);
        limit = Math.Min(maxAmount, limit);
        return Math.Max(0m, Math.Floor(limit));
    }

    // 支払額(設定)の変更。uuid が使う契約の設定を変える(参加者が変えても、契約の設定が変わる)。
    public async Task<ApiResult<ServerLoan>> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        if (paymentAmount < 0m)
            return ApiResult<ServerLoan>.Fail(ErrorCode.ValidationError);

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan>(async db =>
        {
            var contractUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, uuid);
            var loan = await repo.GetOrCreateForUpdateAsync(db, contractUuid);
            loan.PaymentAmount = paymentAmount;
            return ApiResult<ServerLoan>.Ok(loan);
        });
    }

    // 運営による残債の上書き。uuid が使う契約の残債を amount にし、差額を SetBorrowAmount として記録する。
    // 支払額(設定)も、上書き後の残債に合わせた初期値に置き換える。
    public async Task<ApiResult<ServerLoan>> SetBorrowAmountAsync(string uuid, decimal amount)
    {
        if (amount < 0m)
            return ApiResult<ServerLoan>.Fail(ErrorCode.BorrowAmountMustBeZeroOrGreater);
        if (await _profileService.GetNameByUuidAsync(uuid) == null)
            return ApiResult<ServerLoan>.Fail(ErrorCode.PlayerNotFound);

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan>(async db =>
        {
            var contractUuid = await ServerLoanRepository.ResolveContractUuidAsync(db, uuid);
            var loan = await repo.GetOrCreateForUpdateAsync(db, contractUuid);

            ServerLoanRepository.AdjustLoanCore(db, loan, amount - loan.BorrowAmount, ServerLoanRepository.ServerLoanLogAction.SetBorrowAmount);
            loan.PaymentAmount = CalculateInitialPaymentAmount(amount, await ServerLoanRepository.HasMembersAsync(db, contractUuid));
            return ApiResult<ServerLoan>.Ok(loan);
        });
    }

    // 契約の日利。参加者がいる契約(linked)は LinkedDailyInterestRate(未設定なら DailyInterestRate)を使う。
    private static decimal GetDailyInterestRate(bool linked)
    {
        return linked ? LinkedDailyInterestRate ?? DailyInterestRate : DailyInterestRate;
    }

    // 1日分の利息: 残債 × 日利(1 円未満は四捨五入)。
    // 利息で残債(元本 + 利息)が MaxDebtAmount を超えないよう、超える分の利息は付けない(MaxDebtAmount が 0 以下なら上限なし)。
    public static decimal CalculateDailyInterestAmount(decimal debt, bool linked = false)
    {
        if (debt <= 0m)
            return 0m;

        var interest = Math.Round(debt * GetDailyInterestRate(linked), 0, MidpointRounding.AwayFromZero);
        if (MaxDebtAmount > 0m)
            interest = Math.Min(interest, MaxDebtAmount - debt);
        return Math.Max(0m, interest);
    }

    // 現在の残債に、定期返済の間隔(1週間)ぶんの利息を日次の計算どおりに付けた場合の利息合計。
    private static decimal CalculateWeeklyInterestAmount(decimal debt, bool linked)
    {
        var debtAfterWeek = debt;
        for (var day = 0; day < RepayIntervalDays; day++)
            debtAfterWeek += CalculateDailyInterestAmount(debtAfterWeek, linked);
        return debtAfterWeek - debt;
    }

    // 支払額(設定)の初期値: 単利で InitialPaymentWeeks 週間分の利息(最低 1 円)。
    // 支払額が未設定の状態で借りたときと、運営が残債を調整したときに設定する。
    private static decimal CalculateInitialPaymentAmount(decimal debt, bool linked)
    {
        if (debt <= 0m)
            return 0m;

        var days = RepayIntervalDays * InitialPaymentWeeks;
        var paymentAmount = Math.Round(debt * GetDailyInterestRate(linked) * days, 0, MidpointRounding.AwayFromZero);
        return Math.Max(1m, paymentAmount);
    }

    // 定期返済の最低返済額: 次のうち最も大きい額(残債を超えない)。
    //  - 残債 × MinPaymentRate
    //  - MinPaymentAmount
    //  - 1週間分の利息 + 残債 × MinPrincipalRate(金利を上げても最低返済額で残債が減るようにする。MinPrincipalRate が 0 以下なら適用しない)
    public static decimal CalculateMinimumPayment(decimal debt, bool stopInterest = false, bool linked = false)
    {
        if (debt <= 0m)
            return 0m;

        var minimum = Math.Max(Math.Ceiling(debt * MinPaymentRate), MinPaymentAmount);
        if (MinPrincipalRate > 0m)
        {
            var weeklyInterest = stopInterest ? 0m : CalculateWeeklyInterestAmount(debt, linked);
            var principal = Math.Ceiling(debt * MinPrincipalRate);
            minimum = Math.Max(minimum, weeklyInterest + principal);
        }

        return Math.Min(debt, minimum);
    }

    // 定期返済で引き落とす額: 支払額(設定)と最低返済額の大きいほう(残債を超えない)。
    private static decimal CalculateScheduledPayment(decimal debt, decimal paymentAmount, bool stopInterest, bool linked)
    {
        if (debt <= 0m)
            return 0m;

        var requested = Math.Max(paymentAmount, CalculateMinimumPayment(debt, stopInterest, linked));
        return Math.Min(requested, debt);
    }

    // 次回の週次返済日時を計算（UTC基準。スケジューラの発火判定と同じ時刻系で揃える）
    public static DateTime GetNextWeeklyRepayDateTime(DateTime? now = null)
    {
        var baseNow = now ?? DateTime.UtcNow;
        var todayStart = baseNow.Date;
        var daysAhead = ((int)WeeklyRepayDay - (int)baseNow.DayOfWeek + 7) % 7;
        var candidate = todayStart.AddDays(daysAhead).Add(WeeklyRepayTime);
        if (candidate <= baseNow)
        {
            candidate = candidate.AddDays(7);
        }
        return candidate;
    }
    
    // 日次利息(契約 1 件分): 残債に 1 日分の利息を加え、Interest として記録する。
    //  - date の分の Interest ログがすでにある契約には付けない(同じ日に 2 回実行されても二重に付かない)。
    //  - 残債が 0 の契約と、利息停止(stop_interest)の契約には付けない。
    // 戻り値は、利息を付けたかどうか。
    private async Task<ApiResult<bool>> AddDailyInterestAsync(string ownerUuid, DateOnly date)
    {
        return await _bank.RunExclusiveAsync<bool>(async db =>
        {
            var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, ownerUuid);
            if (loan == null || loan.StopInterest)
                return ApiResult<bool>.Ok(false);
            if (await ServerLoanRepository.HasInterestLogOnDateAsync(db, ownerUuid, date))
                return ApiResult<bool>.Ok(false);

            var interest = CalculateDailyInterestAmount(loan.BorrowAmount, await ServerLoanRepository.HasMembersAsync(db, ownerUuid));
            if (interest <= 0m)
                return ApiResult<bool>.Ok(false);

            ServerLoanRepository.AdjustLoanCore(db, loan, interest, ServerLoanRepository.ServerLoanLogAction.Interest);
            return ApiResult<bool>.Ok(true);
        });
    }

    // 日次利息を全契約へ加算する(スケジューラから毎日呼ばれる)。
    public async Task RunDailyInterestForAllAsync(DateOnly? today = null)
    {
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await AddDailyInterestAsync(loan.Uuid, date);
        }
    }

    // 週次返済を全ローンへ実行（スケジューラから呼ばれる）
    public async Task RunWeeklyRepayForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await RepayScheduledAsync(loan.Uuid);
        }
    }

    // 延滞中のローンから延滞分を回収する(スケジューラから毎日呼ばれる)。
    // 回収は未回収分までしか行わないため、同日に複数回実行されても二重回収にならない。
    public async Task RunOverdueCollectionForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            if (loan.BorrowAmount <= 0m)
                continue;
            await CollectOverdueAsync(loan.Uuid);
        }
    }

    private static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.02m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
        MaxDebtAmount = s.GetValue("MaxDebtAmount", 10000000m);
        GrowthFactor = s.GetValue("GrowthFactor", 0.5m);
        MaxGrowthRate = s.GetValue("MaxGrowthRate", 0.1m);
        FailureCutRate = s.GetValue("FailureCutRate", 0.5m);
        MinPaymentRate = s.GetValue("MinPaymentRate", 0.02m);
        MinPaymentAmount = s.GetValue("MinPaymentAmount", 10000m);
        MinPrincipalRate = s.GetValue("MinPrincipalRate", 0.01m);
        LinkedMaxAmount = s.GetValue<decimal?>("LinkedMaxAmount");
        LinkedDailyInterestRate = s.GetValue<decimal?>("LinkedDailyInterestRate");

        if (TimeSpan.TryParse(s["DailyInterestTime"], out var dit))
            DailyInterestTime = dit;
        if (TimeSpan.TryParse(s["WeeklyRepayTime"], out var wrt))
            WeeklyRepayTime = wrt;
        if (Enum.TryParse<DayOfWeek>(s["WeeklyRepayDay"], true, out var wrd))
            WeeklyRepayDay = wrd;
    }
}
