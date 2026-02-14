using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class LoanRepository(BankDbContext db)
{
    public enum CollateralReleaseReason
    {
        CollectorCollect,
        BorrowerReturn,
    }

    public async Task<Loan?> GetByIdAsync(int id)
    {
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<Loan?> GetByIdForUpdateAsync(int id)
    {
        return await db.Loans
            .FromSqlInterpolated($"SELECT * FROM loan_table WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync();
    }

    public async Task<List<Loan>> GetByBorrowerUuidAsync(string borrowUuid, int limit, int offset)
    {
        return await db.Loans
            .AsNoTracking()
            .Where(x => x.BorrowUuid == borrowUuid)
            .OrderByDescending(x => x.BorrowDate).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Loan> AddAsync(Loan entity)
    {
        await db.Loans.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<Loan?> AdjustAmountAsync(int id, decimal delta)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null) return null;

        loan.Amount += delta;
        if (loan.Amount < 0m) loan.Amount = 0m;

        await db.SaveChangesAsync();
        return loan;
    }

    public async Task<bool> DeleteByIdAsync(int id)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null)
            return false;

        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> SetCollateralReleaseReasonAsync(int id, CollateralReleaseReason reason)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
        if (loan == null)
            return 0;

        var entry = db.Entry(loan);
        entry.Property<DateTime?>("CollateralReleasedAt").CurrentValue = DateTime.UtcNow;
        entry.Property<string?>("CollateralReleaseReason").CurrentValue = reason.ToString();
        return await db.SaveChangesAsync();
    }
}
