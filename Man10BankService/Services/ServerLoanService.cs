using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

// サーバーローン(リボ、/mrevo): サーバーがプレイヤーに貸すリボルビングローン。
// 仕様の全体は Man10Bank(プラグイン)リポジトリの docs/ServerLoan.md にまとめている。
//
// 用語
//  - 契約: 残債を持つ単位(server_loan_tbl の 1 行)。持ち主を「本垢」と呼ぶ。
//  - 参加者: 他のプレイヤーの契約に参加しているアカウント(server_loan_member)。
//    参加者の操作は、すべて参加先の契約に対して行う(読み替えは ServerLoanRepository.ResolveContractUuidAsync)。
//  - 残債: 契約の借入残高(borrow_amount)。利息を含む。
//  - 借入枠: 残債の上限。DB には保存せず、契約のログ(server_loan_log)から毎回計算する。
//  - 延滞: 直近の定期返済のログが失敗(RepayFailure)である状態。これも保存せず、ログで判定する。
//
// ファイルの分け方(すべて同じ partial class)
//  - ServerLoanService.cs             参照と、プレイヤー・運営の操作(借入・手動返済・支払額の変更・残債の上書き)
//  - ServerLoanService.Scheduled.cs   スケジューラが動かす処理(日次利息・定期返済・延滞分の回収)
//  - ServerLoanService.Link.cs        契約の共有(参加の申請・承認・脱退、運営による強制リンク)
//  - ServerLoanService.BorrowLimit.cs 借入枠の計算
//  - ServerLoanService.Calculation.cs 利息と返済額の計算
//  - ServerLoanService.Settings.cs    設定値
//
// 書き込みの決まり
//  - 残債や口座を変える処理は、すべて BankService.RunExclusiveAsync(1 本の直列ワーカー + 1 トランザクション)の中で行う。
//    処理が成功(Ok)を返したときだけコミットされ、失敗(Fail)や例外ではロールバックされる。
//  - 行ロックは「契約(server_loan_tbl)→ 口座(user_bank)」の順に取る。
//  - 残債を変えるときは必ず ServerLoanRepository.AdjustLoanCore を通し、同時にログを残す。
public partial class ServerLoanService
{
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

    // ===== 参照 =====

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

    // ===== プレイヤーの操作 =====

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

    // ===== 運営の操作 =====

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
}
