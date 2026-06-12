using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ServerLoanService
{
    private static decimal DailyInterestRate { get; set; } = 0.001m;
    private static decimal MinAmount { get; set; } = 100000m;
    private static decimal MaxAmount { get; set; } = 3000000m;
    private static int RepayWindow { get; set; } = 10;
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
    
    public async Task<ApiResult<ServerLoan?>> BorrowAsync(string uuid, decimal amount)
    {
        try
        {
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.ValidationError);

            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var limitRes = await CalculateBorrowLimitAsync(uuid);
            if (!limitRes.IsSuccess)
                return ApiResult<ServerLoan?>.Fail(limitRes.Code);
            var limit = limitRes.Data;
            
            var currentData = await repo.GetOrCreateByUuidAsync(uuid);
            if (currentData.BorrowAmount + amount > limit)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.BorrowLimitExceeded);

            var resolvedPlayer = await _profileService.GetNameByUuidAsync(uuid) ?? string.Empty;
            var updated = await repo.AdjustLoanAsync(uuid, resolvedPlayer, amount, ServerLoanRepository.ServerLoanLogAction.Borrow);

            var dp = await _bank.DepositAsync(new DepositRequest
            {
                Uuid = uuid,
                Amount = amount,
                PluginName = "server_loan",
                Note = "loan_borrow",
                DisplayNote = "サーバーローン借入",
                Server = "system"
            });

            if (!dp.IsSuccess) return ApiResult<ServerLoan?>.Fail(dp.Code);
            
            var paymentAmount = CalculateRepaymentAmount(amount);
            
            if (updated == null || updated.PaymentAmount > 0m) return ApiResult<ServerLoan?>.Ok(updated);
            
            var set = await repo.SetPaymentAmountAsync(uuid, paymentAmount);
            return ApiResult<ServerLoan?>.Ok(set ?? updated);
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
    
    public async Task<ApiResult<ServerLoan?>> RepayAsync(string uuid, decimal? payAmount)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var loan = await repo.GetOrCreateByUuidAsync(uuid);
            var remain = loan.BorrowAmount;
            if (remain <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.NoRepaymentNeeded);

            var requested = payAmount is > 0m ? payAmount.Value : loan.PaymentAmount;
            if (requested <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.PaymentAmountNotSet);

            var amount = Math.Min(requested, remain);
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.Fail(ErrorCode.PaymentAmountZero);

            var withdrawResult = await _bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = uuid,
                Amount = amount,
                PluginName = "server_loan",
                Note = "loan_repay",
                DisplayNote = "サーバーローン返済",
                Server = "system"
            });

            if (withdrawResult.IsSuccess)
            {
                var updated = await repo.AdjustLoanAsync(uuid, loan.Player, -amount, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                return ApiResult<ServerLoan?>.Ok(updated);
            }

            await repo.AdjustLoanAsync(uuid, loan.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
            return ApiResult<ServerLoan?>.Fail(withdrawResult.Code);
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
    
    public async Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid)
    {
        var borrowable = 0m;
        try
        {
            var repo = new ServerLoanRepository(_dbFactory, _profileService);
            var fetch = Math.Min(1000, RepayWindow * 20);
            var logs = await repo.GetLogsAsync(uuid, fetch);

            var success = logs
                .Where(l => l.Action == ServerLoanRepository.ServerLoanLogAction.RepaySuccess.ToString())
                .Take(RepayWindow)
                .Select(l => l.Amount)
                .ToList();

            var failure = logs
                .Where(l => l.Action == ServerLoanRepository.ServerLoanLogAction.RepayFailure.ToString())
                .Take(RepayWindow)
                .Select(l => l.Amount)
                .ToList();

            if (success.Count > 0)
            {
                var avg = success.Average();
                borrowable = avg * 5m;
            }

            if (failure.Count > 0)
            {
                var minFail = failure.Min();
                borrowable = minFail;
            }
            
            borrowable = Math.Max(MinAmount, borrowable);
            borrowable = Math.Min(MaxAmount, borrowable);

            if (borrowable < 0m) borrowable = 0m;
            return ApiResult<decimal>.Ok(borrowable);
        }
        catch (Exception)
        {
            return ApiResult<decimal>.Fail(ErrorCode.UnexpectedError);
        }
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

    // 次回の週次返済日時を計算（ローカル時刻基準）
    public static DateTime GetNextWeeklyRepayDateTime(DateTime? now = null)
    {
        var baseNow = now ?? DateTime.Now;
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
    
    // 日次利息を全ローンへ加算（スケジューラから呼ばれる）
    public async Task RunDailyInterestForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory, _profileService);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
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
            await RepayAsync(loan.Uuid, null);
        }
    }

    private static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.001m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
        RepayWindow = s.GetValue("RepayWindow", 10);

        if (TimeSpan.TryParse(s["DailyInterestTime"], out var dit))
            DailyInterestTime = dit;
        if (TimeSpan.TryParse(s["WeeklyRepayTime"], out var wrt))
            WeeklyRepayTime = wrt;
        if (Enum.TryParse<DayOfWeek>(s["WeeklyRepayDay"], true, out var wrd))
            WeeklyRepayDay = wrd;
    }
}
