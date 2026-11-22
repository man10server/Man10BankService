using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ServerLoanService
{
    private static decimal DailyInterestRate { get; set; } = 0.001m;
    private static decimal MinAmount { get; set; } = 100000m;
    private static decimal MaxAmount { get; set; } = 3000000m;
    private static decimal GrowthFactor { get; set; } = 0.5m;
    private static decimal MaxGrowthRate { get; set; } = 0.5m;
    private static decimal FailureCutRate { get; set; } = 0.5m;
    private static TimeSpan DailyInterestTime { get; set; } = new(2, 0, 0); // 02:00
    private static TimeSpan WeeklyRepayTime { get; set; } = new(3, 0, 0);   // 03:00
    private static DayOfWeek WeeklyRepayDay { get; set; } = DayOfWeek.Monday;

    // スケジューラ(BackgroundService)が参照する実行時刻設定
    public static TimeSpan DailyInterestTimeOfDay => DailyInterestTime;
    public static TimeSpan WeeklyRepayTimeOfDay => WeeklyRepayTime;
    public static DayOfWeek WeeklyRepayDayOfWeek => WeeklyRepayDay;

    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bank;
    private readonly IPlayerProfileService _profileService;

    public ServerLoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService, IConfiguration config)
    {
        Configure(config);

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

        return ApiResult<PaymentInfoResponse>.Ok(new PaymentInfoResponse(nextRepay, perDay));
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

            var requested = payAmount is > 0m ? payAmount.Value : loan.PaymentAmount;
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
    
    public async Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var logs = await repo.GetScheduledRepayLogsAsync(uuid);
            return ApiResult<decimal>.Ok(CalculateBorrowLimit(logs));
        }
        catch (Exception)
        {
            return ApiResult<decimal>.Fail(ErrorCode.UnexpectedError);
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
    // 成功: 返済額 × GrowthFactor だけ増える(1回の増加は現在の枠 × MaxGrowthRate まで)。
    // 失敗: 現在の枠 × FailureCutRate に下がる。
    private static decimal CalculateBorrowLimit(IEnumerable<ServerLoanLog> scheduledRepayLogs)
    {
        var failure = ServerLoanRepository.ServerLoanLogAction.RepayFailure.ToString();
        var limit = ClampBorrowLimit(MinAmount);

        foreach (var log in scheduledRepayLogs)
        {
            if (log.Action == failure)
            {
                limit *= FailureCutRate;
            }
            else
            {
                // 返済成功ログの金額は残債の増減(負数)で記録されるため絶対値で扱う。
                var paid = Math.Abs(log.Amount);
                limit += Math.Min(paid * GrowthFactor, limit * MaxGrowthRate);
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
    public static decimal CalculateDailyInterestAmount(decimal borrowAmount)
    {
        if (borrowAmount <= 0m) return 0m;
        var interest = borrowAmount * DailyInterestRate;
        var rounded = Math.Round(interest, 0, MidpointRounding.AwayFromZero);
        return rounded < 0m ? 0m : rounded;
    }

    private static decimal CalculateRepaymentAmount(decimal borrowAmount)
    {
        if (borrowAmount <= 0m)
            return 0m;

        var paymentAmount = Math.Round(borrowAmount * DailyInterestRate * 7m * 2m, 0, MidpointRounding.AwayFromZero);
        return Math.Max(1m, paymentAmount);
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

    private static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.001m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
        GrowthFactor = s.GetValue("GrowthFactor", 0.5m);
        MaxGrowthRate = s.GetValue("MaxGrowthRate", 0.5m);
        FailureCutRate = s.GetValue("FailureCutRate", 0.5m);

        if (TimeSpan.TryParse(s["DailyInterestTime"], out var dit))
            DailyInterestTime = dit;
        if (TimeSpan.TryParse(s["WeeklyRepayTime"], out var wrt))
            WeeklyRepayTime = wrt;
        if (Enum.TryParse<DayOfWeek>(s["WeeklyRepayDay"], true, out var wrd))
            WeeklyRepayDay = wrd;
    }
}
