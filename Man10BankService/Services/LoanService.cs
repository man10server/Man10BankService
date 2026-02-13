using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Man10BankService.Services;

public class LoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService)
{
    public async Task<ApiResult<Loan>> GetByIdAsync(int id)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var repo = new LoanRepository(db);
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
            await using var db = await dbFactory.CreateDbContextAsync();
            var repo = new LoanRepository(db);
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
        if (string.IsNullOrWhiteSpace(request.LendUuid) || string.IsNullOrWhiteSpace(request.BorrowUuid))
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
        if (request.LendUuid == request.BorrowUuid)
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
        if (request.BorrowAmount <= 0m)
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
        if (request.RepayAmount <= 0m)
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);
        if (request.RepayAmount < request.BorrowAmount)
            return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

        var (lendName, borrowName) = await GetNamesAsync(request.LendUuid, request.BorrowUuid);
        if (lendName == null || borrowName == null)
            return ApiResult<Loan?>.NotFound(ErrorCode.PlayerNotFound);

        var w = await bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = request.LendUuid,
            Amount = request.BorrowAmount,
            PluginName = "user_loan",
            Note = "user_loan_lend_withdraw",
            DisplayNote = "個人間貸付(出金)",
            Server = "system"
        });

        if (w.StatusCode != 200)
            return new ApiResult<Loan?>(w.StatusCode, w.Code);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var repo = new LoanRepository(db);
            var entity = new Loan
            {
                LendPlayer = lendName,
                LendUuid = request.LendUuid,
                BorrowPlayer = borrowName,
                BorrowUuid = request.BorrowUuid,
                Amount = request.RepayAmount,
                BorrowDate = DateTime.UtcNow,
                PaybackDate = request.PaybackDate,
                CollateralItem = request.CollateralItem ?? string.Empty,
                CollateralReleased = false,
            };
            await repo.AddAsync(entity);

            var deposit = await bank.DepositAsync(new DepositRequest
            {
                Uuid = request.BorrowUuid,
                Amount = request.BorrowAmount,
                PluginName = "user_loan",
                Note = "user_loan_borrow_deposit",
                DisplayNote = "個人間貸付(入金)",
                Server = "system"
            });

            if (deposit.StatusCode == 200)
                return ApiResult<Loan?>.Ok(entity);

            await repo.DeleteByIdAsync(entity.Id);
        }
        catch (Exception)
        {
        }

        await bank.DepositAsync(new DepositRequest
        {
            Uuid = request.LendUuid,
            Amount = request.BorrowAmount,
            PluginName = "user_loan",
            Note = "user_loan_compensate_refund",
            DisplayNote = "個人間貸付(補償返金)",
            Server = "system"
        });
        return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
    }

    public async Task<ApiResult<LoanRepayResponse>> RepayAsync(int id, string collectorUuid)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await BeginTransactionIfSupportedAsync(db);
            var repo = new LoanRepository(db);
            var loan = await repo.GetByIdForUpdateAsync(id);
            if (loan == null)
                return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.LoanNotFound);

            if (loan.CollateralReleased)
                return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.CollateralAlreadyReleased);
            
            if (loan.PaybackDate > DateTime.UtcNow)
                return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.BeforePaybackDate);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return await RepayWithoutCollateralAsync(db, tx, loan, collectorUuid);
            return await RepayWithCollateralAsync(db, tx, loan, collectorUuid);
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

    private async Task<ApiResult<LoanRepayResponse>> RepayWithoutCollateralAsync(
        BankDbContext db,
        IDbContextTransaction? tx,
        Loan loan,
        string collectorUuid)
    {
        var bal = await bank.GetBalanceAsync(loan.BorrowUuid);
        if (bal.StatusCode != 200)
            return new ApiResult<LoanRepayResponse>(bal.StatusCode, bal.Code);

        var toCollect = Math.Min(bal.Data, loan.Amount);
        if (toCollect <= 0m)
            return ApiResult<LoanRepayResponse>.BadRequest(ErrorCode.ValidationError);

        var (borrowPlayer, collectPlayer) = await GetNamesAsync(loan.BorrowUuid, collectorUuid);
        if (borrowPlayer == null || collectPlayer == null)
            return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.PlayerNotFound);

        var withdraw = await WithdrawBorrowerAsync(
            loan.BorrowUuid,
            toCollect,
            "user_loan_collect_withdraw",
            "個人間貸付 回収(出金)");
        if (withdraw.StatusCode != 200)
            return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.InsufficientFunds);

        var rollbackAmount = loan.Amount;
        var remainingAmount = Math.Max(0m, loan.Amount - toCollect);
        return await CompletePaidRepayAsync(
            db,
            tx,
            loan,
            collectorUuid,
            collectedAmount: toCollect,
            remainingAmount: remainingAmount,
            rollbackAmount: rollbackAmount,
            collectorDepositNote: "user_loan_collect_deposit",
            collectorDepositDisplayNote: "個人間貸付 回収(入金)");
    }

    private async Task<ApiResult<LoanRepayResponse>> RepayWithCollateralAsync(
        BankDbContext db,
        IDbContextTransaction? tx,
        Loan loan,
        string collectorUuid)
    {
        if (loan.CollateralReleased)
            return ApiResult<LoanRepayResponse>.Conflict(ErrorCode.CollateralAlreadyReleased);

        if (loan.Amount <= 0m)
            return ApiResult<LoanRepayResponse>.BadRequest(ErrorCode.NoRepaymentNeeded);

        var (borrowPlayer, collectPlayer) = await GetNamesAsync(loan.BorrowUuid, collectorUuid);
        if (borrowPlayer == null || collectPlayer == null)
            return ApiResult<LoanRepayResponse>.NotFound(ErrorCode.PlayerNotFound);

        var amountToCollect = loan.Amount;
        var withdraw = await WithdrawBorrowerAsync(
            loan.BorrowUuid,
            amountToCollect,
            "user_loan_full_collect_withdraw",
            "個人間貸付 一括回収(出金)");

        if (withdraw.StatusCode == 409)
        {
            var collateral = loan.CollateralItem;
            loan.CollateralReleased = true;
            await PersistLoanAmountAsync(db, tx, loan, 0m);

            var dto = new LoanRepayResponse(
                LoanId: loan.Id,
                Outcome: LoanRepayOutcome.CollateralCollected,
                CollectedAmount: 0m,
                RemainingAmount: loan.Amount,
                CollateralItem: string.IsNullOrWhiteSpace(collateral) ? null : collateral
            );
            return ApiResult<LoanRepayResponse>.Ok(dto);
        }

        if (withdraw.StatusCode != 200)
            return new ApiResult<LoanRepayResponse>(withdraw.StatusCode, withdraw.Code);

        return await CompletePaidRepayAsync(
            db,
            tx,
            loan,
            collectorUuid,
            collectedAmount: amountToCollect,
            remainingAmount: 0m,
            rollbackAmount: amountToCollect,
            collectorDepositNote: "user_loan_full_collect_deposit",
            collectorDepositDisplayNote: "個人間貸付 一括回収(入金)");
    }

    private async Task<ApiResult<LoanRepayResponse>> CompletePaidRepayAsync(
        BankDbContext db,
        IDbContextTransaction? tx,
        Loan loan,
        string collectorUuid,
        decimal collectedAmount,
        decimal remainingAmount,
        decimal rollbackAmount,
        string collectorDepositNote,
        string collectorDepositDisplayNote)
    {
        try
        {
            await PersistLoanAmountAsync(db, tx, loan, remainingAmount);
            var deposit = await DepositCollectorAsync(
                collectorUuid,
                collectedAmount,
                collectorDepositNote,
                collectorDepositDisplayNote);
            if (deposit.StatusCode != 200)
            {
                await RecoverAfterRepayTransferFailureAsync(db, loan, rollbackAmount, collectedAmount);
                return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);
            }

            return ApiResult<LoanRepayResponse>.Ok(CreatePaidRepayResponse(loan, collectedAmount));
        }
        catch (Exception)
        {
            await RecoverAfterRepayTransferFailureAsync(db, loan, rollbackAmount, collectedAmount);
            return ApiResult<LoanRepayResponse>.Error(ErrorCode.UnexpectedError);
        }
    }

    private async Task<(string? firstPlayerName, string? secondPlayerName)> GetNamesAsync(string firstUuid, string secondUuid)
    {
        var firstPlayerName = await profileService.GetNameByUuidAsync(firstUuid);
        var secondPlayerName = await profileService.GetNameByUuidAsync(secondUuid);
        return (firstPlayerName, secondPlayerName);
    }

    private Task<ApiResult<decimal>> WithdrawBorrowerAsync(string borrowerUuid, decimal amount, string note, string displayNote)
    {
        return bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = borrowerUuid,
            Amount = amount,
            PluginName = "user_loan",
            Note = note,
            DisplayNote = displayNote,
            Server = "system"
        });
    }

    private Task<ApiResult<decimal>> DepositCollectorAsync(string collectorUuid, decimal amount, string note, string displayNote)
    {
        return bank.DepositAsync(new DepositRequest
        {
            Uuid = collectorUuid,
            Amount = amount,
            PluginName = "user_loan",
            Note = note,
            DisplayNote = displayNote,
            Server = "system"
        });
    }

    private static async Task PersistLoanAmountAsync(BankDbContext db, IDbContextTransaction? tx, Loan loan, decimal amount)
    {
        loan.Amount = amount;
        await db.SaveChangesAsync();
        if (tx != null)
            await tx.CommitAsync();
    }

    private async Task RecoverAfterRepayTransferFailureAsync(
        BankDbContext db,
        Loan loan,
        decimal rollbackAmount,
        decimal compensateAmount)
    {
        try
        {
            loan.Amount = rollbackAmount;
            await db.SaveChangesAsync();
        }
        catch
        {
        }

        await CompensateBorrowerAsync(loan.BorrowUuid, compensateAmount);
    }

    private Task<ApiResult<decimal>> CompensateBorrowerAsync(string borrowerUuid, decimal amount)
    {
        return bank.DepositAsync(new DepositRequest
        {
            Uuid = borrowerUuid,
            Amount = amount,
            PluginName = "user_loan",
            Note = "user_loan_compensate_refund",
            DisplayNote = "個人間貸付(補償返金)",
            Server = "system"
        });
    }

    private static LoanRepayResponse CreatePaidRepayResponse(Loan loan, decimal collectedAmount)
    {
        return new LoanRepayResponse(
            LoanId: loan.Id,
            Outcome: LoanRepayOutcome.Paid,
            CollectedAmount: collectedAmount,
            RemainingAmount: loan.Amount,
            CollateralItem: null
        );
    }

    public async Task<ApiResult<Loan?>> ReleaseCollateralAsync(int id, string borrowerUuid)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await BeginTransactionIfSupportedAsync(db);
            var repo = new LoanRepository(db);
            var loan = await repo.GetByIdForUpdateAsync(id);
            
            if (loan == null)
                return ApiResult<Loan?>.NotFound(ErrorCode.LoanNotFound);
            
            if (!string.Equals(loan.BorrowUuid, borrowerUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<Loan?>.BadRequest(ErrorCode.ValidationError);

            if (loan.Amount > 0m)
                return ApiResult<Loan?>.Conflict(ErrorCode.LoanNotRepaid);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return ApiResult<Loan?>.Conflict(ErrorCode.CollateralNotFound);

            if (loan.CollateralReleased)
                return ApiResult<Loan?>.Conflict(ErrorCode.CollateralAlreadyReleased);

            loan.CollateralReleased = true;
            await db.SaveChangesAsync();
            if (tx != null)
                await tx.CommitAsync();
            
            var updatedLoan = await repo.GetByIdAsync(id);
            if (updatedLoan == null)
                return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
            return ApiResult<Loan?>.Ok(updatedLoan);
        }
        catch (Exception)
        {
            return ApiResult<Loan?>.Error(ErrorCode.UnexpectedError);
        }
    }

    private static async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(BankDbContext db)
    {
        var provider = db.Database.ProviderName;
        if (provider is null || !provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return null;

        return await db.Database.BeginTransactionAsync();
    }
}
