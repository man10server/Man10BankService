using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Responses;
using Man10BankService.Models.Requests;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Services;

public class LoanService(IDbContextFactory<BankDbContext> dbFactory, BankService bank, IPlayerProfileService profileService)
{
    private enum CollateralReleaseReason
    {
        CollectorCollect,
        BorrowerReturn,
    }

    public async Task<ApiResult<Loan>> GetByIdAsync(int id)
    {
        try
        {
            var repo = new LoanRepository(dbFactory);
            var loan = await repo.GetByIdAsync(id);
            return loan == null ? ApiResult<Loan>.Fail(ErrorCode.LoanNotFound) : ApiResult<Loan>.Ok(loan);
        }
        catch (Exception)
        {
            return ApiResult<Loan>.Fail(ErrorCode.UnexpectedError);
        }
    }

    public async Task<ApiResult<List<Loan>>> GetByBorrowerUuidAsync(string borrowUuid, int limit = 100, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(borrowUuid))
            return ApiResult<List<Loan>>.Fail(ErrorCode.BorrowerUuidRequired);
        if (limit is < 1 or > 1000)
            return ApiResult<List<Loan>>.Fail(ErrorCode.LimitOutOfRange);
        if (offset < 0)
            return ApiResult<List<Loan>>.Fail(ErrorCode.OffsetOutOfRange);

        try
        {
            var repo = new LoanRepository(dbFactory);
            var list = await repo.GetByBorrowerUuidAsync(borrowUuid, limit, offset);
            return ApiResult<List<Loan>>.Ok(list);
        }
        catch (Exception)
        {
            return ApiResult<List<Loan>>.Fail(ErrorCode.UnexpectedError);
        }
    }

    // 個人間貸付の作成: 単一トランザクションで「貸手出金+MoneyLog→貸付INSERT→借手入金+MoneyLog→Commit」。
    // 補償 Saga を廃止し原子性を担保する。ロック順序: user_bank 行を uuid 昇順でロック。
    public async Task<ApiResult<Loan?>> CreateAsync(LoanCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LendUuid))
            return ApiResult<Loan?>.Fail(ErrorCode.LenderUuidRequired);
        if (string.IsNullOrWhiteSpace(request.BorrowUuid))
            return ApiResult<Loan?>.Fail(ErrorCode.BorrowerUuidRequired);
        if (string.Equals(request.LendUuid, request.BorrowUuid, StringComparison.OrdinalIgnoreCase))
            return ApiResult<Loan?>.Fail(ErrorCode.LenderAndBorrowerMustDiffer);
        if (request.BorrowAmount <= 0m)
            return ApiResult<Loan?>.Fail(ErrorCode.BorrowAmountMustBePositive);
        if (request.RepayAmount <= 0m)
            return ApiResult<Loan?>.Fail(ErrorCode.RepayAmountMustBePositive);
        if (request.RepayAmount <= request.BorrowAmount)
            return ApiResult<Loan?>.Fail(ErrorCode.RepayAmountMustExceedBorrowAmount);

        var (lendName, borrowName) = await GetNamesAsync(request.LendUuid, request.BorrowUuid);
        if (lendName == null || borrowName == null)
            return ApiResult<Loan?>.Fail(ErrorCode.PlayerNotFound);

        return await bank.RunExclusiveAsync<Loan?>(async db =>
        {
            // user_bank 行を uuid 昇順でロック(デッドロック防止)
            await LockUserBanksInOrderAsync(db, request.LendUuid, request.BorrowUuid);

            // 貸手の残高不足チェック(行ロック下)
            var lendBank = await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == request.LendUuid);
            var lendBalance = lendBank?.Balance ?? 0m;
            if (lendBalance < request.BorrowAmount)
                return ApiResult<Loan?>.Fail(ErrorCode.InsufficientFunds);

            // 貸手出金 + MoneyLog
            await BankRepository.ChangeBalanceCoreAsync(
                db, request.LendUuid, lendName, -request.BorrowAmount,
                "user_loan", "user_loan_lend_withdraw", "個人間貸付(出金)", "system");

            // 貸付 INSERT
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
            db.Loans.Add(entity);

            // 借手入金 + MoneyLog
            await BankRepository.ChangeBalanceCoreAsync(
                db, request.BorrowUuid, borrowName, request.BorrowAmount,
                "user_loan", "user_loan_borrow_deposit", "個人間貸付(入金)", "system");

            return ApiResult<Loan?>.Ok(entity);
        });
    }

    // 個人間貸付の返済(回収): 単一トランザクションで「貸付行ロック→借手出金+MoneyLog→
    // 回収者入金+MoneyLog→貸付残額更新→Commit」。補償 Saga を廃止する。
    // ロック順序: 自リソース(loan)行→user_bank 行(uuid 昇順)。
    public async Task<ApiResult<LoanRepayResponse>> RepayAsync(int id, string collectorUuid)
    {
        if (string.IsNullOrWhiteSpace(collectorUuid))
            return ApiResult<LoanRepayResponse>.Fail(ErrorCode.CollectorUuidRequired);

        return await bank.RunExclusiveAsync<LoanRepayResponse>(async db =>
        {
            // 1) 貸付行ロック
            var loan = await DbLockHelper.GetLoanForUpdateAsync(db, id);
            if (loan == null)
                return ApiResult<LoanRepayResponse>.Fail(ErrorCode.LoanNotFound);
            if (loan.CollateralReleased)
                return ApiResult<LoanRepayResponse>.Fail(ErrorCode.CollateralAlreadyReleased);
            if (loan.Amount <= 0m)
                return ApiResult<LoanRepayResponse>.Fail(ErrorCode.NoRepaymentNeeded);
            if (loan.PaybackDate > DateTime.UtcNow)
                return ApiResult<LoanRepayResponse>.Fail(ErrorCode.BeforePaybackDate);

            var (borrowPlayer, collectPlayer) = await GetNamesAsync(loan.BorrowUuid, collectorUuid);
            if (borrowPlayer == null || collectPlayer == null)
                return ApiResult<LoanRepayResponse>.Fail(ErrorCode.PlayerNotFound);

            // 2) user_bank 行を uuid 昇順でロック
            await LockUserBanksInOrderAsync(db, loan.BorrowUuid, collectorUuid);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return await RepayWithoutCollateralAsync(db, loan, collectorUuid, borrowPlayer, collectPlayer);
            return await RepayWithCollateralAsync(db, loan, collectorUuid, borrowPlayer, collectPlayer);
        });
    }

    // 担保なし: 借手の残高で回収可能な分だけ回収する(部分回収あり)。
    private static async Task<ApiResult<LoanRepayResponse>> RepayWithoutCollateralAsync(
        BankDbContext db, Loan loan, string collectorUuid, string borrowPlayer, string collectPlayer)
    {
        var borrowBank = await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == loan.BorrowUuid);
        var borrowBalance = borrowBank?.Balance ?? 0m;

        var toCollect = Math.Min(borrowBalance, loan.Amount);
        if (toCollect <= 0m)
            return ApiResult<LoanRepayResponse>.Fail(ErrorCode.InsufficientFunds);

        // 借手出金
        await BankRepository.ChangeBalanceCoreAsync(
            db, loan.BorrowUuid, borrowPlayer, -toCollect,
            "user_loan", "user_loan_collect_withdraw", "個人間貸付 回収(出金)", "system");

        // 回収者入金
        await BankRepository.ChangeBalanceCoreAsync(
            db, collectorUuid, collectPlayer, toCollect,
            "user_loan", "user_loan_collect_deposit", "個人間貸付 回収(入金)", "system");

        loan.Amount = Math.Max(0m, loan.Amount - toCollect);

        return ApiResult<LoanRepayResponse>.Ok(new LoanRepayResponse(
            LoanId: loan.Id,
            Outcome: LoanRepayOutcome.Paid,
            CollectedAmount: toCollect,
            RemainingAmount: loan.Amount,
            CollateralItem: null));
    }

    // 担保あり: 全額回収を試み、残高不足かつ猶予期間超過なら担保を没収する。
    private static async Task<ApiResult<LoanRepayResponse>> RepayWithCollateralAsync(
        BankDbContext db, Loan loan, string collectorUuid, string borrowPlayer, string collectPlayer)
    {
        var amountToCollect = loan.Amount;

        var borrowBank = await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == loan.BorrowUuid);
        var borrowBalance = borrowBank?.Balance ?? 0m;

        if (borrowBalance >= amountToCollect)
        {
            await BankRepository.ChangeBalanceCoreAsync(
                db, loan.BorrowUuid, borrowPlayer, -amountToCollect,
                "user_loan", "user_loan_full_collect_withdraw", "個人間貸付 一括回収(出金)", "system");

            await BankRepository.ChangeBalanceCoreAsync(
                db, collectorUuid, collectPlayer, amountToCollect,
                "user_loan", "user_loan_full_collect_deposit", "個人間貸付 一括回収(入金)", "system");

            loan.Amount = 0m;

            return ApiResult<LoanRepayResponse>.Ok(new LoanRepayResponse(
                LoanId: loan.Id,
                Outcome: LoanRepayOutcome.Paid,
                CollectedAmount: amountToCollect,
                RemainingAmount: 0m,
                CollateralItem: null));
        }

        // 残高不足: 猶予期間(借入期間と同じ長さ)を過ぎていれば担保没収。
        var period = loan.PaybackDate - loan.BorrowDate;
        var collateralUnlockAt = loan.PaybackDate + period;
        if (DateTime.UtcNow < collateralUnlockAt)
            return ApiResult<LoanRepayResponse>.Fail(ErrorCode.InsufficientFunds);

        var collateral = loan.CollateralItem;
        loan.CollateralReleased = true;
        loan.Amount = 0m;
        SetCollateralReleaseMetadata(loan, CollateralReleaseReason.CollectorCollect);

        return ApiResult<LoanRepayResponse>.Ok(new LoanRepayResponse(
            LoanId: loan.Id,
            Outcome: LoanRepayOutcome.CollateralCollected,
            CollectedAmount: 0m,
            RemainingAmount: loan.Amount,
            CollateralItem: string.IsNullOrWhiteSpace(collateral) ? null : collateral));
    }

    public async Task<ApiResult<Loan?>> ReleaseCollateralAsync(int id, string borrowerUuid)
    {
        if (string.IsNullOrWhiteSpace(borrowerUuid))
            return ApiResult<Loan?>.Fail(ErrorCode.BorrowerUuidRequired);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            var loan = await LoanRepository.GetByIdForUpdateAsync(db, id);

            if (loan == null)
                return ApiResult<Loan?>.Fail(ErrorCode.LoanNotFound);

            if (!string.Equals(loan.BorrowUuid, borrowerUuid, StringComparison.OrdinalIgnoreCase))
                return ApiResult<Loan?>.Fail(ErrorCode.BorrowerMismatch);

            if (loan.Amount > 0m)
                return ApiResult<Loan?>.Fail(ErrorCode.LoanNotRepaid);

            if (string.IsNullOrWhiteSpace(loan.CollateralItem))
                return ApiResult<Loan?>.Fail(ErrorCode.CollateralNotFound);

            if (loan.CollateralReleased)
                return ApiResult<Loan?>.Fail(ErrorCode.CollateralAlreadyReleased);

            loan.CollateralReleased = true;
            SetCollateralReleaseMetadata(loan, CollateralReleaseReason.BorrowerReturn);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return ApiResult<Loan?>.Ok(loan);
        }
        catch (Exception)
        {
            return ApiResult<Loan?>.Fail(ErrorCode.UnexpectedError);
        }
    }

    private async Task<(string? firstPlayerName, string? secondPlayerName)> GetNamesAsync(string firstUuid, string secondUuid)
    {
        var firstPlayerName = await profileService.GetNameByUuidAsync(firstUuid);
        var secondPlayerName = await profileService.GetNameByUuidAsync(secondUuid);
        return (firstPlayerName, secondPlayerName);
    }

    // user_bank 行を uuid 昇順でロックする(デッドロック防止。DESIGN 2.3 ロック順序規約)。
    private static async Task LockUserBanksInOrderAsync(BankDbContext db, string firstUuid, string secondUuid)
    {
        var ordered = string.CompareOrdinal(firstUuid, secondUuid) <= 0
            ? new[] { firstUuid, secondUuid }
            : new[] { secondUuid, firstUuid };
        foreach (var uuid in ordered)
            await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);
    }

    private static void SetCollateralReleaseMetadata(Loan loan, CollateralReleaseReason reason)
    {
        loan.CollateralReleasedAt = DateTime.UtcNow;
        loan.CollateralReleaseReason = reason.ToString();
    }
}
