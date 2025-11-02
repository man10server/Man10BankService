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

    public async Task<ServerLoan?> AdjustLoanAsync(string uuid, string player, decimal delta, ServerLoanLogAction action)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan == null)
        {
            if (action != ServerLoanLogAction.Borrow || delta <= 0m)
                return null;

            loan = new ServerLoan
            {
                Uuid = uuid,
                Player = string.IsNullOrWhiteSpace(player) ? string.Empty : player,
                BorrowAmount = delta,
                PaymentAmount = 0m,
                FailedPayment = 0,
                StopInterest = false,
                BorrowDate = DateTime.UtcNow,
                LastPayDate = DateTime.UtcNow,
            };
            await db.ServerLoans.AddAsync(loan);
            await AddLogAsync(db, loan.Uuid, loan.Player, action, delta);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return loan;
        }
        switch (action)
        {
            case ServerLoanLogAction.Borrow:
                loan.BorrowAmount += delta;
                break;
            case ServerLoanLogAction.Interest:
                if (!loan.StopInterest)
                {
                    loan.BorrowAmount += delta;
                }
                break;
            case ServerLoanLogAction.RepaySuccess:
                loan.BorrowAmount += delta;
                loan.LastPayDate = DateTime.UtcNow;
                break;
            case ServerLoanLogAction.RepayFailure:
                loan.FailedPayment += 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
        await AddLogAsync(db, loan.Uuid, loan.Player, action, delta);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }
    
    public async Task<List<ServerLoanLog>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
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
