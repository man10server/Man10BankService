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
            return loan == null ? ApiResult<Loan>.NotFound(ErrorCode.LoanNotFound) : ApiResult<Loan>.Ok(loan);
        }
        catch (Exception)
        {
            return ApiResult<Loan>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<Loan>>> GetByBorrowerUuidAsync(string borrowUuid, int limit = 100, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(borrowUuid))
            return ApiResult<List<Loan>>.BadRequest(ErrorCode.ValidationError);
        if (limit is < 1 or > 1000)
            return ApiResult<List<Loan>>.BadRequest(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<Loan>>.BadRequest(ErrorCode.OffsetOutOfRange);

        try
        {
            var repo = new LoanRepository(dbFactory);
            var list = await repo.GetByBorrowerUuidAsync(borrowUuid, limit, offset);
            return ApiResult<List<Loan>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<Loan>>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<Loan?>> CreateAsync(LoanCreateRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.LendUuid) || string.IsNullOrWhiteSpace(request.BorrowUuid))
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
            if (request.LendUuid == request.BorrowUuid)
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
            if (request.Amount <= 0m)
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

            var lendName = await MinecraftProfileService.GetNameByUuidAsync(request.LendUuid) ?? string.Empty;
            var borrowName = await MinecraftProfileService.GetNameByUuidAsync(request.BorrowUuid) ?? string.Empty;

            var w = await bank.WithdrawAsync(new WithdrawRequest
            {
                Uuid = request.LendUuid,
                Amount = request.Amount,
                PluginName = "user_loan",
                Note = "user_loan_lend_withdraw",
                DisplayNote = "個人間貸付(出金)",
                Server = "system"
            });
            
            if (w.StatusCode != 200)
                return new ApiResult<Loan?>(w.StatusCode, w.Code);
            
            var repo = new LoanRepository(dbFactory);
            try
            {
                var entity = await repo.CreateAsync(
                    lendName,
                    request.LendUuid,
                    borrowName,
                    request.BorrowUuid,
                    request.Amount,
                    request.PaybackDate,
                    request.CollateralItem);
                
                var deposit = await bank.DepositAsync(new DepositRequest
                {
                    Uuid = request.BorrowUuid,
                    Amount = request.Amount,
                    PluginName = "user_loan",
                    Note = "user_loan_borrow_deposit",
                    DisplayNote = "個人間貸付(入金)",
                    Server = "system"
                });

                if (deposit.StatusCode == 200) return ApiResult<Loan?>.Ok(entity);
                await repo.DeleteByIdAsync(entity.Id);
                throw new Exception("borrower_deposit_failed");
            }
            catch (Exception)
            {
                await bank.DepositAsync(new DepositRequest
                {
                    Uuid = request.LendUuid,
                    Amount = request.Amount,
                    PluginName = "user_loan",
                    Note = "user_loan_compensate_refund",
                    DisplayNote = "個人間貸付(補償返金)",
                    Server = "system"
                });
                return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
            }
        }
        catch (Exception)
        {
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<Loan?>> RepayAsync(int id, string collectorUuid)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan?>.NotFound(ErrorCode.LoanNotFound);
            
            if (loan.PaybackDate > DateTime.UtcNow)
                return ApiResult<Loan?>.Conflict(ErrorCode.BeforePaybackDate);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return await RepayWithoutCollateralAsync(loan, collectorUuid);
            return await RepayWithCollateralAsync(loan, collectorUuid);
        }
        catch (ArgumentException)
        {
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<ApiResult<Loan?>> RepayWithoutCollateralAsync(Loan loan, string collectorUuid)
    {
        var bal = await bank.GetBalanceAsync(loan.BorrowUuid);
        if (bal.StatusCode != 200)
            return new ApiResult<Loan?>(bal.StatusCode, bal.Code);

        var toCollect = Math.Min(bal.Data, loan.Amount);
        if (toCollect <= 0m)
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

        // 名前解決はロギング用途のみ。取得失敗時は空文字で続行。
        _ = await MinecraftProfileService.GetNameByUuidAsync(loan.BorrowUuid) ?? loan.BorrowPlayer;
        _ = await MinecraftProfileService.GetNameByUuidAsync(collectorUuid) ?? string.Empty;

        var w = await bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = loan.BorrowUuid,
            Amount = toCollect,
            PluginName = "user_loan",
            Note = "user_loan_collect_withdraw",
            DisplayNote = "個人間貸付 回収(出金)",
            Server = "system"
        });
        if (w.StatusCode != 200)
            return ApiResult<Loan?>.Conflict(ErrorCode.InsufficientFunds);

        try
        {
            var repo = new LoanRepository(dbFactory);
            var updated = await repo.AdjustAmountAsync(loan.Id, -toCollect);
            if (updated == null) throw new Exception("返済レコード更新に失敗しました。");
            
            var deposit = await bank.DepositAsync(new DepositRequest
            {
                Uuid = collectorUuid,
                Amount = toCollect,
                PluginName = "user_loan",
                Note = "user_loan_collect_deposit",
                DisplayNote = "個人間貸付 回収(入金)",
                Server = "system"
            });

            return deposit.StatusCode == 200 ? ApiResult<Loan?>.Ok(updated) : ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
        catch (Exception)
        {
            await bank.DepositAsync(new DepositRequest
            {
                Uuid = loan.BorrowUuid,
                Amount = toCollect,
                PluginName = "user_loan",
                Note = "user_loan_compensate_refund",
                DisplayNote = "個人間貸付(補償返金)",
                Server = "system"
            });
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<ApiResult<Loan?>> RepayWithCollateralAsync(Loan loan, string collectorUuid)
    {
        if (loan.Amount <= 0m)
            return ApiResult<Loan?>.BadRequest(ErrorCode.NoRepaymentNeeded);

        _ = await MinecraftProfileService.GetNameByUuidAsync(loan.BorrowUuid) ?? loan.BorrowPlayer;
        _ = await MinecraftProfileService.GetNameByUuidAsync(collectorUuid) ?? string.Empty;

        var withdraw = await bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = loan.BorrowUuid,
            Amount = loan.Amount,
            PluginName = "user_loan",
            Note = "user_loan_full_collect_withdraw",
            DisplayNote = "個人間貸付 一括回収(出金)",
            Server = "system"
        });

        // 残高不足などで一括回収できない場合は担保回収
        if (withdraw.StatusCode == 409)
        {
            var repo = new LoanRepository(dbFactory);
            var afterCollateral = await repo.ClearDebtAndCollectCollateralAsync(loan.Id);
            return afterCollateral == null ? ApiResult<Loan?>.Error(ErrorCode.UnexpectedError) : ApiResult<Loan?>.Ok(afterCollateral, ErrorCode.Conflict);
        }

        // その他のエラーはそのまま返す
        if (withdraw.StatusCode != 200)
            return new ApiResult<Loan?>(withdraw.StatusCode, withdraw.Code);

        try
        {
            var repo = new LoanRepository(dbFactory);
            var cleared = await repo.AdjustAmountAsync(loan.Id, -loan.Amount);
            if (cleared == null) throw new Exception("repay_update_failed");

            var deposit = await bank.DepositAsync(new DepositRequest
            {
                Uuid = collectorUuid,
                Amount = loan.Amount,
                PluginName = "user_loan",
                Note = "user_loan_full_collect_deposit",
                DisplayNote = "個人間貸付 一括回収(入金)",
                Server = "system"
            });

            if (deposit.StatusCode != 200) throw new Exception("collector_deposit_failed");
            return ApiResult<Loan?>.Ok(cleared);
        }
        catch (Exception)
        {
            // 返金（補償）
            await bank.DepositAsync(new DepositRequest
            {
                Uuid = loan.BorrowUuid,
                Amount = loan.Amount,
                PluginName = "user_loan",
                Note = "user_loan_compensate_refund",
                DisplayNote = "個人間貸付(補償返金)",
                Server = "system"
            });
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<Loan?>> ReleaseCollateralAsync(int id, string borrowerUuid)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<Loan?>.NotFound(ErrorCode.LoanNotFound);

            if (!string.Equals(loan.BorrowUuid, borrowerUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

            if (loan.Amount > 0m)
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

            var ok = await repo.CollectCollateralAsync(id);
            if (!ok)
                return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);

            var updated = await repo.GetByIdAsync(id);
            return ApiResult<Loan?>.Ok(updated);
        }
        catch (Exception)
        {
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }
}
