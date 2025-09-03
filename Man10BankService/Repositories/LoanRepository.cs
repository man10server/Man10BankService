using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class LoanRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<Loan?> GetByIdAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<Loan>> GetByBorrowerUuidAsync(string borrowUuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        if (offset < 0) offset = 0;

        await using var db = await factory.CreateDbContextAsync();
        return await db.Loans
            .AsNoTracking()
            .Where(x => x.BorrowUuid == borrowUuid)
            .OrderByDescending(x => x.BorrowDate).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Loan> CreateAsync(
        string lendPlayer,
        string lendUuid,
        string borrowPlayer,
        string borrowUuid,
        decimal amount,
        DateTime paybackDate,
        string? collateralItem)
    {
        if (amount <= 0m) throw new ArgumentException("金額は 0 より大きい必要があります。", nameof(amount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var entity = new Loan
        {
            LendPlayer = lendPlayer,
            LendUuid = lendUuid,
            BorrowPlayer = borrowPlayer,
            BorrowUuid = borrowUuid,
            Amount = amount,
            BorrowDate = DateTime.UtcNow,
            PaybackDate = paybackDate,
            CollateralItem = collateralItem ?? string.Empty,
        };

        await db.Loans.AddAsync(entity);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return entity;
    }

    public async Task<Loan?> AdjustAmountAsync(int id, decimal delta)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null) return null;

        loan.Amount += delta;
        if (loan.Amount < 0m) loan.Amount = 0m;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }

    public async Task<bool> DeleteByIdAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null)
            return false;

        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> CollectCollateralAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null)
            return false;

        if (string.IsNullOrWhiteSpace(loan.CollateralItem))
            return false; // すでに担保なし

        loan.CollateralItem = string.Empty;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return true;
    }
}
