using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Services;
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
        SetBorrowAmount,
    }

    public async Task<ServerLoan> GetOrCreateByUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var loan = await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan != null) return loan;
        
        var player = await MinecraftProfileService.GetNameByUuidAsync(uuid);
        if (player == null) throw new ArgumentException("指定された UUID のプレイヤーが見つかりません。", nameof(uuid));
        loan = new ServerLoan
        {
            Uuid = uuid,
            Player = player,
            BorrowAmount = 0m,
            PaymentAmount = 0m,
            LastPayDate = DateTime.UtcNow,
            FailedPayment = 0,
            StopInterest = false,
        };
        await db.ServerLoans.AddAsync(loan);
        await db.SaveChangesAsync();

        return loan;
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

        var loan = await GetOrCreateByUuidAsync(uuid);
        switch (action)
        {
            case ServerLoanLogAction.Borrow:
                if (loan.BorrowAmount == 0m)
                {
                    loan.LastPayDate = DateTime.UtcNow;
                }
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
        db.Update(loan);
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

        var loan = await GetOrCreateByUuidAsync(uuid);

        loan.PaymentAmount = paymentAmount;
        db.Update(loan);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }

    public async Task<ServerLoan?> SetBorrowAmountAsync(string uuid, decimal borrowAmount, decimal paymentAmount)
    {
        if (borrowAmount < 0m) throw new ArgumentException("借入残額は 0 以上で指定してください。", nameof(borrowAmount));
        if (paymentAmount < 0m) throw new ArgumentException("支払額は 0 以上で指定してください。", nameof(paymentAmount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await GetOrCreateByUuidAsync(uuid);
        var delta = borrowAmount - loan.BorrowAmount;

        loan.BorrowAmount = borrowAmount;
        loan.PaymentAmount = paymentAmount;
        db.Update(loan);
        await AddLogAsync(db, loan.Uuid, loan.Player, ServerLoanLogAction.SetBorrowAmount, delta);
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
