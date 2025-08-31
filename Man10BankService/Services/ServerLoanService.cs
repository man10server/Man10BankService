using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class ServerLoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank)
{
    private static decimal DailyInterestRate { get; set; } = 0.001m;
    private static decimal MinAmount { get; set; } = 100000m;
    private static decimal MaxAmount { get; set; } = 3000000m;
    private static int RepayWindow { get; set; } = 10;

    public static void Configure(IConfiguration configuration)
    {
        var s = configuration.GetSection("ServerLoan");
        DailyInterestRate = s.GetValue("DailyInterestRate", 0.001m);
        MinAmount = s.GetValue("MinAmount", 100000m);
        MaxAmount = s.GetValue("MaxAmount", 3000000m);
        RepayWindow = s.GetValue("RepayWindow", 10);
    }

    public async Task<ApiResult<ServerLoan>> GetByUuidAsync(string uuid)
    {
        try
        {
            var repo = new ServerLoanRepository(dbFactory);
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
    
    public async Task<ApiResult<ServerLoan?>> BorrowAsync(ServerLoanBorrowRequest req)
    {
        try
        {
            if (req.Amount <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("借入金額は 0 より大きい必要があります。");

            var repo = new ServerLoanRepository(dbFactory);
            var loan = await repo.GetByUuidAsync(req.Uuid);
            if (loan == null)
                return ApiResult<ServerLoan?>.NotFound("借入データが見つかりません。");

            // 先に入金処理（入金失敗時は借入を記録しない）
            var dp = await bank.DepositAsync(new DepositRequest
            {
                Uuid = req.Uuid,
                Player = req.Player,
                Amount = req.Amount,
                PluginName = "server_loan",
                Note = "loan_borrow",
                DisplayNote = "サーバーローン借入",
                Server = "system"
            });

            if (dp.StatusCode != 200)
            {
                return new ApiResult<ServerLoan?>(dp.StatusCode, dp.Message);
            }

            var updated = await repo.AdjustLoanAsync(req.Uuid, req.Player, req.Amount, ServerLoanRepository.ServerLoanLogAction.Borrow);
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
    
    public async Task<ApiResult<ServerLoan?>> RepayAsync(ServerLoanRepayRequest req)
    {
        try
        {
            var repo = new ServerLoanRepository(dbFactory);
            var loan = await repo.GetByUuidAsync(req.Uuid);
            if (loan == null)
                return ApiResult<ServerLoan?>.NotFound("借入データが見つかりません。");

            // 借入残高を算出（BorrowAmount - PaymentAmount）
            var remain = loan.BorrowAmount - loan.PaymentAmount;
            if (remain <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("返済は不要です。すでに完済しています。");

            // リクエストの支払額（未指定または 0 以下なら既定の PaymentAmount）
            var requested = req.Amount is > 0m ? req.Amount.Value : loan.PaymentAmount;
            if (requested <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("支払額が設定されていません。支払額を指定するか、PaymentAmount を設定してください。");

            // 過払い防止のため、残高を上限にクリップ
            var amount = Math.Min(requested, remain);
            if (amount <= 0m)
                return ApiResult<ServerLoan?>.BadRequest("支払額が 0 円のため処理を実行できません。");

            var wd = await bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = req.Uuid,
                Player = req.Player,
                Amount = amount,
                PluginName = "server_loan",
                Note = "loan_repay",
                DisplayNote = "サーバーローン返済",
                Server = "system"
            });

            if (wd.StatusCode == 200)
            {
                var updated = await repo.AdjustLoanAsync(req.Uuid, req.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepaySuccess);
                return ApiResult<ServerLoan?>.Ok(updated);
            }

            await repo.AdjustLoanAsync(req.Uuid, req.Player, amount, ServerLoanRepository.ServerLoanLogAction.RepayFailure);
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
    
    public async Task<ApiResult<decimal>> CalculateBorrowLimitAsync(string uuid, int? window = null)
    {
        var borrowable = 0m;
        try
        {
            var repo = new ServerLoanRepository(dbFactory);
            var loan = await repo.GetByUuidAsync(uuid);
            if (loan == null)
                return ApiResult<decimal>.Ok(MinAmount);

            var w = window.GetValueOrDefault(RepayWindow);
            if (w < 1) w = 1; if (w > 1000) w = 1000;

            // ある程度多めに取得して、返済ログだけ抽出
            var fetch = Math.Min(1000, w * 20);
            var logs = await repo.GetLogsAsync(uuid, fetch);

            var success = logs
                .Where(l => l.Action == ServerLoanRepository.ServerLoanLogAction.RepaySuccess.ToString())
                .Take(w)
                .Select(l => l.Amount)
                .ToList();

            var failure = logs
                .Where(l => l.Action == ServerLoanRepository.ServerLoanLogAction.RepayFailure.ToString())
                .Take(w)
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
}
