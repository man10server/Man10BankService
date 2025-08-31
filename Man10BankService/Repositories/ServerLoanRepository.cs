using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ServerLoanRepository(IDbContextFactory<BankDbContext> factory)
{
    public enum ServerLoanLogAction
    {
        Borrow,
        RepaySuccess,
        RepayFailure,
        Interest,
    }

    public async Task<ServerLoan?> GetByUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    public async Task<List<ServerLoan>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().ToListAsync();
    }

    public async Task<ServerLoan?> AdjustLoanAsync(string uuid, string player, decimal amount, ServerLoanLogAction action)
    {
        if (amount <= 0m) throw new ArgumentException("金額は 0 より大きい必要があります。", nameof(amount));

        await using var db = await factory.CreateDbContextAsync();

        var loan = await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan == null)
        {
            return null;
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        switch (action)
        {
            case ServerLoanLogAction.Borrow:
                loan.BorrowAmount += amount;
                loan.LastPayDate = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await AddLogAsync(db, loan.Uuid, loan.Player, ServerLoanLogAction.Borrow, amount);
                break;
            case ServerLoanLogAction.Interest:
                if (!loan.StopInterest)
                {
                    loan.BorrowAmount += amount;
                    loan.LastPayDate = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    await AddLogAsync(db, loan.Uuid, loan.Player, ServerLoanLogAction.Interest, amount);
                }
                break;
            case ServerLoanLogAction.RepaySuccess:
                loan.PaymentAmount += amount;
                loan.LastPayDate = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await AddLogAsync(db, loan.Uuid, loan.Player, ServerLoanLogAction.RepaySuccess, amount);
                break;
            case ServerLoanLogAction.RepayFailure:
                loan.FailedPayment += 1;
                await db.SaveChangesAsync();
                await AddLogAsync(db, loan.Uuid, string.IsNullOrWhiteSpace(player) ? loan.Player : player, ServerLoanLogAction.RepayFailure, amount);
                return loan;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
        await tx.CommitAsync();
        return loan;
    }
    
    public async Task<List<ServerLoanLog>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        if (limit < 1) limit = 1;
        if (limit > 1000) limit = 1000;
        if (offset < 0) offset = 0;

        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    // 週次等の支払額を設定する
    public async Task<ServerLoan?> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        if (paymentAmount < 0m) throw new ArgumentException("支払額は 0 以上で指定してください。", nameof(paymentAmount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan == null) return null;

        loan.PaymentAmount = paymentAmount;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }
    
    private static async Task AddLogAsync(BankDbContext db, string uuid, string player, ServerLoanLogAction action, decimal amount)
    {
        var log = new ServerLoanLog
        {
            Uuid = uuid,
            Player = player,
            Action = action.ToString(),
            Amount = amount,
            // Date は DB 既定値
        };
        await db.ServerLoanLogs.AddAsync(log);
    }
}
