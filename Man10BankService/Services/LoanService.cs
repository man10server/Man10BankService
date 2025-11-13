using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
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

            var lendName = await MinecraftProfileService.GetNameByUuidAsync(request.LendUuid);
            var borrowName = await MinecraftProfileService.GetNameByUuidAsync(request.BorrowUuid);
            if (lendName == null || borrowName == null)
                return ApiResult<Loan?>.NotFound(ErrorCode.PlayerNotFound);

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

    public async Task<ApiResult<LoanRepayResponse>> RepayAsync(int id, string collectorUuid)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            if (loan == null)
                return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.LoanNotFound);
            
            if (loan.PaybackDate > DateTime.UtcNow)
                return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.BeforePaybackDate);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return await RepayWithoutCollateralAsync(loan, collectorUuid);
            return await RepayWithCollateralAsync(loan, collectorUuid);
        }
        catch (ArgumentException)
        {
            return ApiResult<LoanRepayResponse>.BadRequest(ErrorCode.ValidationError);
        }
        catch (Exception)
        {
            return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<ApiResult<LoanRepayResponse>> RepayWithoutCollateralAsync(Loan loan, string collectorUuid)
    {
        var bal = await bank.GetBalanceAsync(loan.BorrowUuid);
        if (bal.StatusCode != 200)
            return new ApiResult<LoanRepayResponse>(bal.StatusCode, bal.Code);

        var toCollect = Math.Min(bal.Data, loan.Amount);
        if (toCollect <= 0m)
            return ApiResult<LoanRepayResponse>.BadRequest(ErrorCode.ValidationError);

        var borrowPlayer = await MinecraftProfileService.GetNameByUuidAsync(loan.BorrowUuid);
        var collectPlayer = await MinecraftProfileService.GetNameByUuidAsync(collectorUuid);
        if (borrowPlayer == null || collectPlayer == null)
        {
            return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.PlayerNotFound);
        }
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
            return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.InsufficientFunds);

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

            if (deposit.StatusCode != 200)
                return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);

            var dto = new LoanRepayResponse(
                LoanId: loan.Id,
                Outcome: LoanRepayOutcome.Paid,
                CollectedAmount: toCollect,
                RemainingAmount: updated.Amount,
                CollateralItem: null
            );
            return ApiResult<LoanRepayResponse>.Ok(dto);
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
            return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<ApiResult<LoanRepayResponse>> RepayWithCollateralAsync(Loan loan, string collectorUuid)
    {
        if (loan.Amount <= 0m)
            return ApiResult<LoanRepayResponse>.BadRequest(ErrorCode.NoRepaymentNeeded);

        var borrowPlayer = await MinecraftProfileService.GetNameByUuidAsync(loan.BorrowUuid);
        var lendPlayer = await MinecraftProfileService.GetNameByUuidAsync(collectorUuid);
        if (borrowPlayer == null || lendPlayer == null)
        {
            return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.PlayerNotFound);
        }
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
            var collateral = loan.CollateralItem;
            var afterCollateral = await repo.ClearDebtAndCollectCollateralAsync(loan.Id);
            if (afterCollateral == null)
                return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);

            var dto = new LoanRepayResponse(
                LoanId: loan.Id,
                Outcome: LoanRepayOutcome.CollateralCollected,
                CollectedAmount: 0m,
                RemainingAmount: afterCollateral.Amount,
                CollateralItem: string.IsNullOrWhiteSpace(collateral) ? null : collateral
            );
            return ApiResult<LoanRepayResponse>.Ok(dto);
        }

        // その他のエラーはそのまま返す
        if (withdraw.StatusCode != 200)
            return new ApiResult<LoanRepayResponse>(withdraw.StatusCode, withdraw.Code);

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

            var dto = new LoanRepayResponse(
                LoanId: loan.Id,
                Outcome: LoanRepayOutcome.Paid,
                CollectedAmount: loan.Amount,
                RemainingAmount: cleared.Amount,
                CollateralItem: null
            );
            return ApiResult<LoanRepayResponse>.Ok(dto);
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
            return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);
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
