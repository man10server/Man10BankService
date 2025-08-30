using Man10BankService.Data;
using Man10BankService.Models;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ServerLoanRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<ServerLoan?> GetByUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    public async Task<ServerLoan?> RepayAsync(string uuid, decimal amount)
    {
        if (amount <= 0m) throw new ArgumentException("返済額は 0 より大きい必要があります。", nameof(amount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan == null) return null;

        loan.PaymentAmount += amount;
        loan.LastPayDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }

    public async Task<ServerLoan?> AddInterestAsync(string uuid, decimal interestAmount)
    {
        if (interestAmount <= 0m) throw new ArgumentException("金利額は 0 より大きい必要があります。", nameof(interestAmount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan == null) return null;
        if (loan.StopInterest) return loan;

        loan.BorrowAmount += interestAmount;
        loan.LastPayDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }
}

