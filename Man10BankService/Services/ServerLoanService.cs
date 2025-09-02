using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
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

    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankService _bank;

    public ServerLoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank,IConfiguration config)
    {
        StartScheduler();
        Configure(config);
        
        _dbFactory = dbFactory;
        _bank = bank;
    }
    
    public void StartScheduler()
    {
        Task.Run(SchedulerLoopAsync);
    }

    public async Task<ApiResult<ServerLoan>> GetByUuidAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<ServerLoan>.NotFound("借入データが見つかりません。");
            return ApiResult<ServerLoan>.Ok(loan);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan>.Error($"借入データの取得に失敗しました: {ex.Message}");
        }
    }
    
    public async Task<ApiResult<List<ServerLoanLog>>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 1000)
            return ApiResult<List<ServerLoanLog>>.BadRequest("limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return ApiResult<List<ServerLoanLog>>.BadRequest("offset は 0 以上で指定してください。");
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var logs = await repo.GetLogsAsync(uuid, limit, offset);
            return ApiResult<List<ServerLoanLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            return ApiResult<List<ServerLoanLog>>.Error($"ログ取得に失敗しました: {ex.Message}");
        }
    }
    
    public async Task<ApiResult<ServerLoan?>> BorrowAsync(string uuid, string player, decimal amount)
    {
        try
        {
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("借入金額は 0 より大きい必要があります。");

            var repo = new ServerLoanRepository(_dbFactory);
            var (statusCode, message, limit) = await CalculateBorrowLimitAsync(uuid);
            if (statusCode != 200)
                return new ApiResult<ServerLoan?>(statusCode, message);
            
            var currentData = await repo.GetByUuidAsync(uuid);
            var currentPayment = currentData?.BorrowAmount ?? 0m;
            if (currentPayment > limit)
                return ApiResult<ServerLoan?>.Conflict("借入可能額を超過しているため借入できません。");

            var updated = await repo.AdjustLoanAsync(uuid, player, amount, ServerLoanRepository.ServerLoanLogAction.Borrow);

            var dp = await _bank.DepositAsync(new DepositRequest
            {
                Uuid = uuid,
                Player = player,
                Amount = amount,
                PluginName = "server_loan",
                Note = "loan_borrow",
                DisplayNote = "サーバーローン借入",
                Server = "system"
            });

            if (dp.StatusCode != 200) return new ApiResult<ServerLoan?>(dp.StatusCode, dp.Message);
            
            var paymentAmount = Math.Round(amount * DailyInterestRate * 7m * 2m, 0, MidpointRounding.AwayFromZero);
            if (paymentAmount < 1m) paymentAmount = 1m;
            
            if (updated == null || updated.PaymentAmount > 0m) return ApiResult<ServerLoan?>.Ok(updated);
            
            var set = await repo.SetPaymentAmountAsync(uuid, paymentAmount);
            if (set != null) updated = set;
            return ApiResult<ServerLoan?>.Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<ServerLoan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan?>.Error($"借入処理に失敗しました: {ex.Message}");
        }
    }
    
    public async Task<ApiResult<ServerLoan?>> RepayAsync(string uuid, decimal? payAmount)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<ServerLoan?>.NotFound("借入データが見つかりません。");

            var remain = loan.BorrowAmount - loan.PaymentAmount;
            if (remain <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("返済は不要です。すでに完済しています。");

            var requested = payAmount is > 0m ? payAmount.Value : loan.PaymentAmount;
            if (requested <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("支払額が設定されていません。支払額を指定するか、PaymentAmount を設定してください。");

            var amount = Math.Min(requested, remain);
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("支払額が 0 円のため処理を実行できません。");

            var wd = await _bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = uuid,
                Player = loan.Player,
                Amount = amount,
                PluginName = "server_loan",
                Note = "loan_repay",
                DisplayNote = "サーバーローン返済",
                Server = "system"
            });

            if (wd.StatusCode == 200)
            {
                var updated = await repo.AdjustLoanAsync(uuid, loan.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                return ApiResult<ServerLoan?>.Ok(updated);
            }

            await repo.AdjustLoanAsync(uuid, loan.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
            return new ApiResult<ServerLoan?>(wd.StatusCode, wd.Message);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<ServerLoan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan?>.Error($"返済処理に失敗しました: {ex.Message}");
        }
    }
    
    public async Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid)
    {
        var borrowable = 0m;
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<decimal>.Ok(MinAmount);

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
        catch (Exception ex)
        {
            return ApiResult<decimal>.Error($"借入可能額の計算に失敗しました: {ex.Message}");
        }
    }
    
    public async Task<ApiResult<ServerLoan?>> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var loan = await repo.SetPaymentAmountAsync(uuid, paymentAmount);
            if (loan == null)
                return ApiResult<ServerLoan?>.NotFound("借入データが見つかりません。");
            return ApiResult<ServerLoan?>.Ok(loan);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<ServerLoan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan?>.Error($"支払額の設定に失敗しました: {ex.Message}");
        }
    }
    
    private async Task<ApiResult<ServerLoan?>> AddDailyInterestAsync(string uuid, string player)
    {
        try
        {
            var repo = new ServerLoanRepository(_dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<ServerLoan?>.NotFound("借入データが見つかりません。");

            if (loan.StopInterest)
                return ApiResult<ServerLoan?>.Ok(loan, "利息計算は停止されています。");

            if (loan.BorrowAmount <= 0m)
                return ApiResult<ServerLoan?>.Ok(loan, "借入額が 0 円のため、利息は追加されません。");

            var interest = loan.BorrowAmount * DailyInterestRate;

            var rounded = Math.Round(interest, 0, MidpointRounding.AwayFromZero);
            if (rounded <= 0m)
                return ApiResult<ServerLoan?>.Ok(loan, "利息が 0 円のため、追加は行いません。");

            var updated = await repo.AdjustLoanAsync(uuid,
                string.IsNullOrWhiteSpace(player) ? loan.Player : player,
                rounded,
                ServerLoanRepository.ServerLoanLogAction.Interest);

            return ApiResult<ServerLoan?>.Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<ServerLoan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<ServerLoan?>.Error($"利息追加に失敗しました: {ex.Message}");
        }
    }
    
    private async Task SchedulerLoopAsync()
    {
        DateOnly? lastDailyRun = null;
        DateOnly? lastWeeklyRun = null;
        while (true)
        {
            try
            {
                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now);

                var dailyDue = now.TimeOfDay >= DailyInterestTime;
                if (dailyDue && lastDailyRun != today)
                {
                    await RunDailyInterestForAllAsync();
                    lastDailyRun = today;
                }

                var weeklyDue = now.DayOfWeek == WeeklyRepayDay && now.TimeOfDay >= WeeklyRepayTime;
                if (weeklyDue && lastWeeklyRun != today)
                {
                    await RunWeeklyRepayForAllAsync();
                    lastWeeklyRun = today;
                }
            }
            catch
            {
                // ignored
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1)); }
            catch (TaskCanceledException) { break; }
        }
    }
    
    private async Task RunDailyInterestForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory);
        var loans = await repo.GetAllAsync();
        foreach (var loan in loans)
        {
            await AddDailyInterestAsync(loan.Uuid, loan.Player);
        }
    }

    private async Task RunWeeklyRepayForAllAsync()
    {
        var repo = new ServerLoanRepository(_dbFactory);
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
