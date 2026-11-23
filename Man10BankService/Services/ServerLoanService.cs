using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ServerLoanService
{
    private static decimal DailyInterestRate { get; set; } = 0.02m;
    private static decimal MinAmount { get; set; } = 100000m;
    private static decimal MaxAmount { get; set; } = 3000000m;
    private static decimal MaxDebtAmount { get; set; } = 10000000m;
    private static decimal GrowthFactor { get; set; } = 0.5m;
    private static decimal MaxGrowthRate { get; set; } = 0.1m;
    private static decimal FailureCutRate { get; set; } = 0.5m;
    private static decimal MinPaymentRate { get; set; } = 0.02m;
    private static decimal MinPaymentAmount { get; set; } = 10000m;
    private static decimal MinPrincipalRate { get; set; } = 0.01m;
    private static TimeSpan DailyInterestTime { get; set; } = new(2, 0, 0); // 02:00
    private static TimeSpan WeeklyRepayTime { get; set; } = new(3, 0, 0);   // 03:00
    private static DayOfWeek WeeklyRepayDay { get; set; } = DayOfWeek.Monday;

    // 定期返済の間隔(日数)。増枠の平均残債と、最低返済額に含める利息の算定に使う。
    private const int RepayIntervalDays = 7;

    // スケジューラ(BackgroundService)が参照する実行時刻設定
    public static TimeSpan DailyInterestTimeOfDay => DailyInterestTime;
    public static TimeSpan WeeklyRepayTimeOfDay => WeeklyRepayTime;
    public static DayOfWeek WeeklyRepayDayOfWeek => WeeklyRepayDay;

    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bank;
    private readonly IPlayerProfileService _profileService;

    public ServerLoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService, IConfiguration config,
        ILogger<ServerLoanService>? logger = null)
    {
        Configure(config);

        if (MinPrincipalRate <= 0m && MinPaymentRate <= DailyInterestRate * 7m)
        {
            logger?.LogWarning(
                "ServerLoan:MinPaymentRate({MinPaymentRate}) が週あたりの金利({WeeklyInterestRate})以下のため、最低返済額では残債が減りません。",
                MinPaymentRate, DailyInterestRate * 7m);
        }

        _dbFactory = dbFactory;
        _bank = bank;
        _profileService = profileService;
    }

    public async Task<ApiResult<ServerLoan>> GetByUuidAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var player = await _profileService.GetNameByUuidAsync(uuid);
            if (player == null)
                return ApiResult<ServerLoan>.Fail(ErrorCode.PlayerNotFound);
            var loan = await repo.GetOrCreateByUuidAsync(uuid);
            return ApiResult<ServerLoan>.Ok(loan);
        }
        catch (Exception)
        {
            return ApiResult<ServerLoan>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 返済情報(次回支払日時・日あたり金利)を計算する。プレイヤー未登録時は PlayerNotFound。
    public async Task<ApiResult<PaymentInfoResponse>> GetPaymentInfoAsync(string uuid)
    {
        var loanRes = await GetByUuidAsync(uuid);
        if (!loanRes.IsSuccess || loanRes.Data is null)
            return ApiResult<PaymentInfoResponse>.Fail(loanRes.Code);

        var loan = loanRes.Data;

        // 日あたりの金利増加額(StopInterest や残債 0 は 0 とする)
        var perDay = loan.StopInterest || loan.BorrowAmount <= 0m
            ? 0m
            : CalculateDailyInterestAmount(loan.BorrowAmount);

        // 次回支払日(設定に基づく週次支払日時の次回)
        var nextRepay = GetNextWeeklyRepayDateTime();

        // 次回の定期返済で引き落とす額(現時点の残債で計算。最低返済額を反映する)
        var nextPayment = CalculateScheduledPayment(loan.BorrowAmount, loan.PaymentAmount, loan.StopInterest);

        return ApiResult<PaymentInfoResponse>.Ok(new PaymentInfoResponse(nextRepay, perDay, nextPayment));
    }

    public async Task<ApiResult<List<ServerLoanLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<ServerLoanLog>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<ServerLoanLog>>.Fail(ErrorCode.OffsetOutOfRange);
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var logs = await repo.GetLogsAsync(uuid, limit, offset);
            return ApiResult<List<ServerLoanLog>>.Ok(logs);
        }
        catch (Exception)
        {
            return ApiResult<List<ServerLoanLog>>.Fail(ErrorCode.UnexpectedError);
        }
    }
    
    // 借入: 単一トランザクションで「server_loan 行ロック→上限チェック→債務加算→
    // user_bank 残高加算+MoneyLog→ServerLoanLog→Commit」を行う(補償 Saga 廃止)。
    // ロック順序規約: 自リソース行(server_loan)→user_bank 行。
    public async Task<ApiResult<ServerLoan?>> BorrowAsync(string uuid, decimal amount)
    {
        if (amount <= 0m)
            return ApiResult<ServerLoan?>.Fail(ErrorCode.ValidationError);

        // 上限額の算定はログ集計のみで残高/債務を変更しないため、トランザクション前に読む。
        var limitRes = await CalculateBorrowLimitAsync(uuid);
        if (!limitRes.IsSuccess)
            return ApiResult<ServerLoan?>.Fail(limitRes.Code);
        var limit = limitRes.Data;

        // 定期返済に失敗したままの間は、次の定期返済が成功するまで新規借入を止める。
        var overdueRes = await IsRepaymentOverdueAsync(uuid);
        if (!overdueRes.IsSuccess)
            return ApiResult<ServerLoan?>.Fail(overdueRes.Code);
        if (overdueRes.Data)
            return ApiResult<ServerLoan?>.Fail(ErrorCode.RepaymentOverdue);

        // 名前解決(ログイン時登録の名簿 → 口座の名前)はワーカー外で行う。
        var resolved = await _bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<ServerLoan?>.Fail(resolved.Code);
        var resolvedPlayer = resolved.Data!;

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan?>(async db =>
        {
            // 1) server_loan 行ロック(MySQL 時 FOR UPDATE)
            var loan = await repo.GetOrCreateForUpdateAsync(db, uuid, resolvedPlayer);

            // 2) ロック下の確定値で上限チェック
            if (loan.BorrowAmount + amount > limit)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.BorrowLimitExceeded);

            // 支払額未設定なら初回返済額を設定(債務加算前の判定)
            var needPayment = loan.PaymentAmount <= 0m;

            // 3) 債務加算 + ServerLoanLog
            ServerLoanRepository.AdjustLoanCore(db, loan, resolvedPlayer, amount, ServerLoanRepository.ServerLoanLogAction.Borrow);

            if (needPayment)
                loan.PaymentAmount = CalculateRepaymentAmount(amount);

            // 4) user_bank 残高加算 + MoneyLog(同一 tx)
            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, resolvedPlayer, amount,
                "server_loan", "loan_borrow", "サーバーローン借入", "system");

            return ApiResult<ServerLoan?>.Ok(loan);
        });
    }

    // 返済: 単一トランザクションで「server_loan 行ロック→残高減算+MoneyLog→債務減算+ログ→Commit」。
    // 出金成功後の債務減算が同一 tx 内のため、片方だけ成立する不整合が発生しない(補償 Saga 廃止)。
    // 与信評価(CalculateBorrowLimitAsync)の対象は週次の定期返済(scheduled)のみ。
    // プレイヤーによる手動返済は EarlyRepay として記録し、残高不足でも失敗に数えない。
    public async Task<ApiResult<ServerLoan?>> RepayAsync(string uuid, decimal? payAmount, bool scheduled = false)
    {
        var resolved = await _bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<ServerLoan?>.Fail(resolved.Code);
        var resolvedPlayer = resolved.Data!;

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        var result = await _bank.RunExclusiveAsync<ServerLoan?>(async db =>
        {
            // 1) server_loan 行ロック
            var loan = await repo.GetOrCreateForUpdateAsync(db, uuid, resolvedPlayer);
            var remain = loan.BorrowAmount;
            if (remain <= 0m)
            {
                // 延滞後に完済済みのまま定期返済日を迎えた場合は、0 円の成功ログを残して延滞を解消する。
                // ログを確定させるため Ok + NoRepaymentNeeded で返し、ラッパ側で失敗へ変換する。
                if (scheduled && await ServerLoanRepository.IsLatestScheduledRepayFailureAsync(db, uuid))
                {
                    ServerLoanRepository.AdjustLoanCore(db, loan, loan.Player, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                    return ApiResult<ServerLoan?>.Ok(loan, ErrorCode.NoRepaymentNeeded);
                }
                return ApiResult<ServerLoan?>.Fail(ErrorCode.NoRepaymentNeeded);
            }

            // 定期返済は設定額と最低返済額の大きいほうを引き落とす。
            var requested = scheduled
                ? CalculateScheduledPayment(remain, loan.PaymentAmount, loan.StopInterest)
                : payAmount is > 0m ? payAmount.Value : loan.PaymentAmount;
            if (requested <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.PaymentAmountNotSet);

            var amount = Math.Min(requested, remain);
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.PaymentAmountZero);

            // 2) user_bank 行ロック下で残高不足を判定
            var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            var balance = bank?.Balance ?? 0m;
            if (balance < amount)
            {
                if (!scheduled)
                    return ApiResult<ServerLoan?>.Fail(ErrorCode.InsufficientFunds);

                // 定期返済の残高不足は返済失敗としてカウントし、失敗ログを残す。
                // この失敗カウントは確定させたいので、Ok + InsufficientFunds コードで返して
                // 同一 tx をコミットさせる(ラッパ側で 409 失敗へ変換する)。
                ServerLoanRepository.AdjustLoanCore(db, loan, loan.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
                return ApiResult<ServerLoan?>.Ok(loan, ErrorCode.InsufficientFunds);
            }

            // 3) 残高減算 + MoneyLog
            await BankRepository.ChangeBalanceCoreAsync(
                db, uuid, resolvedPlayer, -amount,
                "server_loan", "loan_repay", "サーバーローン返済", "system");

            // 4) 債務減算 + 返済ログ
            var action = scheduled
                ? ServerLoanRepository.ServerLoanLogAction.RepaySuccess
                : ServerLoanRepository.ServerLoanLogAction.EarlyRepay;
            ServerLoanRepository.AdjustLoanCore(db, loan, loan.Player, -amount, action);

            return ApiResult<ServerLoan?>.Ok(loan);
        });

        // 残高不足の失敗ログ・延滞解消ログは確定させつつ、呼び出し側へは失敗として返す。
        if (result.IsSuccess && result.Code is ErrorCode.InsufficientFunds or ErrorCode.NoRepaymentNeeded)
            return ApiResult<ServerLoan?>.Fail(result.Code);
        return result;
    }
    
    // 延滞分の回収: 直近の定期返済が失敗のままのローンについて、未回収分を銀行残高のある分だけ回収する。
    // 失敗額を回収しきるか残債が 0 になったら、0 円の成功ログを残して延滞を解消する(借入枠は増えない)。
    private async Task<ApiResult<ServerLoan?>> CollectOverdueAsync(string uuid)
    {
        var resolved = await _bank.ResolvePlayerNameAsync(uuid);
        if (!resolved.IsSuccess)
            return ApiResult<ServerLoan?>.Fail(resolved.Code);
        var resolvedPlayer = resolved.Data!;

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        return await _bank.RunExclusiveAsync<ServerLoan?>(async db =>
        {
            var loan = await repo.GetOrCreateForUpdateAsync(db, uuid, resolvedPlayer);
            if (loan.BorrowAmount <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.NoRepaymentNeeded);

            var uncollected = await ServerLoanRepository.GetUncollectedOverdueAmountAsync(db, uuid);
            if (uncollected is null)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.NoRepaymentNeeded);

            var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
            var balance = bank?.Balance ?? 0m;
            var target = Math.Min(uncollected.Value, loan.BorrowAmount);
            var amount = Math.Min(target, balance);
            if (target > 0m && amount <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.InsufficientFunds);

            if (amount > 0m)
            {
                await BankRepository.ChangeBalanceCoreAsync(
                    db, uuid, resolvedPlayer, -amount,
                    "server_loan", "loan_overdue_collect", "サーバーローン延滞分の回収", "system");

                ServerLoanRepository.AdjustLoanCore(db, loan, loan.Player, -amount, ServerLoanRepository.ServerLoanLogAction.OverdueCollect);
            }

            if (amount >= target)
                ServerLoanRepository.AdjustLoanCore(db, loan, loan.Player, 0m, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);

            return ApiResult<ServerLoan?>.Ok(loan);
        });
    }

    public async Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var (debt, logs) = await repo.GetBorrowLimitSourceAsync(uuid);
            return ApiResult<decimal>.Ok(CalculateBorrowLimit(logs, debt));
        }
        catch (Exception)
        {
            return ApiResult<decimal>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 残債があり延滞中(直近の定期返済が失敗のまま)かどうか。ローンが無いプレイヤーは false で、行は作らない。
    public async Task<ApiResult<bool>> HasOverdueDebtAsync(string uuid)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return ApiResult<bool>.Ok(await ServerLoanRepository.HasOverdueDebtAsync(db, uuid));
        }
        catch (Exception)
        {
            return ApiResult<bool>.Fail(ErrorCode.UnexpectedError);
        }
    }

    private async Task<ApiResult<bool>> IsRepaymentOverdueAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            return ApiResult<bool>.Ok(await repo.IsLatestScheduledRepayFailureAsync(uuid));
        }
        catch (Exception)
        {
            return ApiResult<bool>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 借入枠は MinAmount から始まり、定期返済の履歴を古い順にたどって決まる。
    // 成功: 「返済額」と「その週の平均残債」の小さいほう × GrowthFactor だけ増える(1回の増加は現在の枠 × MaxGrowthRate まで)。
    // 失敗: 現在の枠 × FailureCutRate に下がる。
    // 平均残債は、前回の定期返済より後かつ直近 RepayIntervalDays 日以内に利息が付いた時点の残債を
    // RepayIntervalDays で割ったもの。利息が付かなかった日は 0 として扱うため、返済の直前だけ借りても枠は増えない。
    private static decimal CalculateBorrowLimit(IReadOnlyList<ServerLoanLog> logs, decimal currentDebt)
    {
        var success = ServerLoanRepository.ServerLoanLogAction.RepaySuccess.ToString();
        var failure = ServerLoanRepository.ServerLoanLogAction.RepayFailure.ToString();
        var interest = ServerLoanRepository.ServerLoanLogAction.Interest.ToString();

        // 各ログの直前の残債を、現在の残債からログの増減を逆算して求める。
        var debtBefore = new decimal[logs.Count];
        var debt = currentDebt;
        for (var i = logs.Count - 1; i >= 0; i--)
        {
            // 返済失敗ログの金額は引き落とそうとした額で、残債は変わらない。
            if (logs[i].Action != failure)
                debt -= logs[i].Amount;
            debtBefore[i] = debt;
        }

        var limit = ClampBorrowLimit(MinAmount);
        var interestDebts = new List<(DateTime Date, decimal Debt)>();

        for (var i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            if (log.Action == interest)
            {
                interestDebts.Add((log.Date, Math.Max(0m, debtBefore[i])));
                continue;
            }

            if (log.Action == failure)
            {
                limit *= FailureCutRate;
                interestDebts.Clear();
            }
            else if (log.Action == success)
            {
                // 返済成功ログの金額は残債の増減(負数)で記録されるため絶対値で扱う。
                // 0 円の成功(延滞解消の記録)は枠を増やさず、その週の利息の記録も消費しない。
                var paid = Math.Abs(log.Amount);
                if (paid <= 0m)
                    continue;

                var from = log.Date.AddDays(-RepayIntervalDays);
                var averageDebt = interestDebts.Where(x => x.Date > from).Sum(x => x.Debt) / RepayIntervalDays;
                limit += Math.Min(Math.Min(paid, averageDebt) * GrowthFactor, limit * MaxGrowthRate);
                interestDebts.Clear();
            }
            else
            {
                continue;
            }

            limit = ClampBorrowLimit(limit);
        }

        return limit;
    }

    private static decimal ClampBorrowLimit(decimal limit)
    {
        limit = Math.Max(MinAmount, limit);
        limit = Math.Min(MaxAmount, limit);
        return Math.Max(0m, Math.Floor(limit));
    }

    public async Task<ApiResult<ServerLoan?>> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var loan = await repo.SetPaymentAmountAsync(uuid, paymentAmount);
            if (loan == null)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.LoanNotFound);
            return ApiResult<ServerLoan?>.Ok(loan);
        }
        catch (ArgumentException)
        {
            return ApiResult<ServerLoan?>.Fail(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<ServerLoan?>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<ServerLoan?>> SetBorrowAmountAsync(string uuid, decimal amount)
    {
        if (amount < 0m)
            return ApiResult<ServerLoan?>.Fail(ErrorCode.BorrowAmountMustBeZeroOrGreater);

        var paymentAmount = CalculateRepaymentAmount(amount);
        if (paymentAmount < 0m)
            return ApiResult<ServerLoan?>.Fail(ErrorCode.BorrowAmountMustBeZeroOrGreater);

        var repo = new ServerLoanRepository(_dbFactory, _profileService);

        try
        {
            var player = await _profileService.GetNameByUuidAsync(uuid);
            if (player == null)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.PlayerNotFound);

            var loan = await repo.SetBorrowAmountAsync(uuid, player, amount, paymentAmount);
            return ApiResult<ServerLoan?>.Ok(loan);
        }
        catch (Exception)
        {
            return ApiResult<ServerLoan?>.Fail(ErrorCode.SetBorrowAmountFailed);
        }
    }
    
    // 金利の丸め規則をサービス内で一元化
    // 利息で残債(元本+利息)が MaxDebtAmount を超えないよう、超える分の利息は付けない(0 以下なら上限なし)。
    public static decimal CalculateDailyInterestAmount(decimal borrowAmount)
    {
        if (borrowAmount <= 0m) return 0m;
        var interest = borrowAmount * DailyInterestRate;
        var rounded = Math.Round(interest, 0, MidpointRounding.AwayFromZero);
        if (MaxDebtAmount > 0m)
            rounded = Math.Min(rounded, MaxDebtAmount - borrowAmount);
        return rounded < 0m ? 0m : rounded;
    }

    private static decimal CalculateRepaymentAmount(decimal borrowAmount)
    {
        if (borrowAmount <= 0m)
            return 0m;

        var paymentAmount = Math.Round(borrowAmount * DailyInterestRate * 7m * 2m, 0, MidpointRounding.AwayFromZero);
        return Math.Max(1m, paymentAmount);
    }

    // 定期返済の最低返済額: 次のうち最も大きい額(残債を超えない)。
    //  - 残債 × MinPaymentRate
    //  - MinPaymentAmount
    //  - 1週間分の利息 + 残債 × MinPrincipalRate(金利を上げても最低返済額で残債が減るようにする。MinPrincipalRate が 0 以下なら適用しない)
    public static decimal CalculateMinimumPayment(decimal borrowAmount, bool stopInterest = false)
    {
        if (borrowAmount <= 0m)
            return 0m;

        var minimum = Math.Max(Math.Ceiling(borrowAmount * MinPaymentRate), MinPaymentAmount);
        if (MinPrincipalRate > 0m)
        {
            var interest = stopInterest ? 0m : CalculateWeeklyInterestAmount(borrowAmount);
            minimum = Math.Max(minimum, interest + Math.Ceiling(borrowAmount * MinPrincipalRate));
        }

        return Math.Min(borrowAmount, minimum);
    }

    // 現在の残債に、定期返済の間隔(1週間)ぶんの利息を日次の計算どおりに付けた場合の利息合計。
    private static decimal CalculateWeeklyInterestAmount(decimal borrowAmount)
    {
        var debt = borrowAmount;
        for (var i = 0; i < RepayIntervalDays; i++)
            debt += CalculateDailyInterestAmount(debt);
        return debt - borrowAmount;
    }

    private static decimal CalculateScheduledPayment(decimal borrowAmount, decimal paymentAmount, bool stopInterest)
    {
        if (borrowAmount <= 0m)
            return 0m;

        var requested = Math.Max(paymentAmount, CalculateMinimumPayment(borrowAmount, stopInterest));
        return Math.Min(requested, borrowAmount);
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
    
    private async Task<ApiResult<ServerLoan?>> AddDailyInterestAsync(string uuid, string player)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var loan = await repo.GetOrCreateByUuidAsync(uuid);
            if (loan.StopInterest)
                return ApiResult<ServerLoan?>.Ok(loan, ErrorCode.InterestStopped);

            if (loan.BorrowAmount <= 0m)
                return ApiResult<ServerLoan?>.Ok(loan, ErrorCode.InterestZero);

            var rounded = CalculateDailyInterestAmount(loan.BorrowAmount);
            if (rounded <= 0m)
                return ApiResult<ServerLoan?>.Ok(loan, ErrorCode.InterestZero);

            var updated = await repo.AdjustLoanAsync(uuid,
                string.IsNullOrWhiteSpace(player) ? loan.Player : player,
                rounded,
                ServerLoanRepository.ServerLoanLogAction.Interest);

            return ApiResult<ServerLoan?>.Ok(updated);
        }
        catch (ArgumentException)
        {
            return ApiResult<ServerLoan?>.Fail(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<ServerLoan?>.Fail(ErrorCode.UnexpectedError);
        }
    }
    
    // 当日分の Interest ログが既に存在するローンが1件でもあるか(検証・テスト用の参照クエリ)。
    // 注意: ローン横断のORなので冪等ガードには使わないこと。スケジューラの冪等性は
    // RunDailyInterestForAllAsync 内のローン単位判定(HasInterestLogOnDateAsync)が担保する。
    public async Task<bool> HasDailyInterestRunAsync(DateOnly date)
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            if (await repo.HasInterestLogOnDateAsync(loan.Uuid, date))
                return true;
        }
        return false;
    }

    // 日次利息を全ローンへ加算（スケジューラから呼ばれる）。
    // 冪等化: 当日分の Interest ログが既にあるローンはスキップし、再起動後の二重課金を防ぐ。
    public async Task RunDailyInterestForAllAsync(DateOnly? today = null)
    {
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            if (await repo.HasInterestLogOnDateAsync(loan.Uuid, date))
                continue;
            await AddDailyInterestAsync(loan.Uuid, loan.Player);
        }
    }

    // 週次返済を全ローンへ実行（スケジューラから呼ばれる）
    public async Task RunWeeklyRepayForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await RepayAsync(loan.Uuid, null, scheduled: true);
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
            if (!await repo.IsLatestScheduledRepayFailureAsync(loan.Uuid))
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

        if (TimeSpan.TryParse(s["DailyInterestTime"], out var dit))
            DailyInterestTime = dit;
        if (TimeSpan.TryParse(s["WeeklyRepayTime"], out var wrt))
            WeeklyRepayTime = wrt;
        if (Enum.TryParse<DayOfWeek>(s["WeeklyRepayDay"], true, out var wrd))
            WeeklyRepayDay = wrd;
    }
}
