using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class LoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank)
{
    public async Task<ApiResult<Loan>> GetByIdAsync(int id)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan>.NotFound("借金データが見つかりません。");
            return ApiResult<Loan>.Ok(loan);
        }
        catch (Exception ex)
        {
            return ApiResult<Loan>.Error($"借金データの取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<List<Loan>>> GetByBorrowerUuidAsync(string borrowUuid, int limit = 100, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(borrowUuid))
            return ApiResult<List<Loan>>.BadRequest("borrowUuid を指定してください。");
        if (limit is < 1 or > 1000)
            return ApiResult<List<Loan>>.BadRequest("limit は 1..1000 の範囲で指定してください。");
        if (offset < 0)
            return ApiResult<List<Loan>>.BadRequest("offset は 0 以上で指定してください。");

        try
        {
            var repo = new LoanRepository(dbFactory);
            var list = await repo.GetByBorrowerUuidAsync(borrowUuid, limit, offset);
            return ApiResult<List<Loan>>.Ok(list);
        }
        catch (Exception ex)
        {
            return ApiResult<List<Loan>>.Error($"借金一覧の取得に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<Loan?>> CreateAsync(
        string lendUuid,
        string lendPlayer,
        string borrowUuid,
        string borrowPlayer,
        decimal amount,
        DateTime paybackDate,
        string collateralItem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(lendUuid) || string.IsNullOrWhiteSpace(borrowUuid))
                return ApiResult<Loan?>.BadRequest("貸手/借手の UUID は必須です。");
            if (lendUuid == borrowUuid)
                return ApiResult<Loan?>.BadRequest("貸手と借手が同一です。");
            if (amount <= 0m)
                return ApiResult<Loan?>.BadRequest("金額は 0 より大きい必要があります。");

            var w = await bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = lendUuid,
                Player = lendPlayer,
                Amount = amount,
                PluginName = "user_loan",
                Note = "user_loan_lend_withdraw",
                DisplayNote = "個人間貸付(出金)",
                Server = "system"
            });
            
            if (w.StatusCode != 200)
                return new ApiResult<Loan?>(w.StatusCode, w.Message);
            
            var repo = new LoanRepository(dbFactory);
            try
            {
                var entity = await repo.CreateAsync(lendPlayer, lendUuid, borrowPlayer, borrowUuid, amount, paybackDate, collateralItem);
                
                var deposit = await bank.DepositAsync(new DepositRequest
                {
                    Uuid = borrowUuid,
                    Player = borrowPlayer,
                    Amount = amount,
                    PluginName = "user_loan",
                    Note = "user_loan_borrow_deposit",
                    DisplayNote = "個人間貸付(入金)",
                    Server = "system"
                });

                if (deposit.StatusCode == 200) return ApiResult<Loan?>.Ok(entity);
                await repo.DeleteByIdAsync(entity.Id);
                throw new Exception("借手への入金に失敗しました: " + deposit.Message);
            }
            catch (Exception ex)
            {
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = lendUuid,
                    Player = lendPlayer,
                    Amount = amount,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return ApiResult<Loan?>.Error($"借金レコードの作成に失敗しました: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return ApiResult<Loan?>.Error($"借金作成に失敗しました: {ex.Message}");
        }
    }

    public async Task<ApiResult<Loan?>> RepayAsync(int id, string collectorUuid)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan?>.NotFound("借金データが見つかりません。");

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return await RepayWithoutCollateralAsync(loan, collectorUuid);
            else
                return await RepayWithCollateralAsync(loan, collectorUuid);
        }
        catch (ArgumentException ex)
        {
            return ApiResult<Loan?>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResult<Loan?>.Error($"返済処理に失敗しました: {ex.Message}");
        }
    }

    private async Task<ApiResult<Loan?>> RepayWithoutCollateralAsync(Loan loan, string collectorUuid)
    {
        var bal = await bank.GetBalanceAsync(loan.BorrowUuid);
        if (bal.StatusCode != 200)
            return new ApiResult<Loan?>(bal.StatusCode, bal.Message);

        var toCollect = Math.Min(bal.Data, loan.Amount);
        if (toCollect <= 0m)
            return ApiResult<Loan?>.BadRequest("回収可能な残高がありません。");

        var w = await bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = loan.BorrowUuid,
            Player = loan.BorrowPlayer,
            Amount = toCollect,
            PluginName = "user_loan",
            Note = "user_loan_collect_withdraw",
            DisplayNote = "個人間貸付 回収(出金)",
            Server = "system"
        });
        if (w.StatusCode != 200)
            return new ApiResult<Loan?>(w.StatusCode, w.Message);

        try
        {
            var repo = new LoanRepository(dbFactory);
            var updated = await repo.AdjustAmountAsync(loan.Id, -toCollect);
            if (updated == null) throw new Exception("返済レコード更新に失敗しました。");
            
            var deposit = await bank.DepositAsync(new DepositRequest
            {
                Uuid = collectorUuid,
                Player = string.Empty,
                Amount = toCollect,
                PluginName = "user_loan",
                Note = "user_loan_collect_deposit",
                DisplayNote = "個人間貸付 回収(入金)",
                Server = "system"
            });

            if (deposit.StatusCode != 200) throw new Exception("回収者への入金に失敗しました: " + deposit.Message);
            
            return ApiResult<Loan?>.Ok(updated);
        }
        catch (Exception e)
        {
            await bank.DepositAsync(new DepositRequest
            {
                Uuid = loan.BorrowUuid,
                Player = loan.BorrowPlayer,
                Amount = toCollect,
                PluginName = "user_loan",
                Note = "user_loan_compensate_refund",
                DisplayNote = "個人間貸付(補償返金)",
                Server = "system"
            });
            return ApiResult<Loan?>.Error(e.Message);
        }
    }

    private Task<ApiResult<Loan?>> RepayWithCollateralAsync(Loan loan, string collectorUuid)
    {
        // 後で詳細実装
        return Task.FromResult(ApiResult<Loan?>.BadRequest("担保回収ロジックは未実装です。"));
    }
}
