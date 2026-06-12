using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ServerLoanRepository(IDbContextFactory<BankDbContext> factory, IPlayerProfileService profileService)
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

        var player = await profileService.GetNameByUuidAsync(uuid);
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

    // トランザクション内で server_loan 行を取得/作成する。
    // 既存行は FOR UPDATE で行ロックする(MySQL 時)。作成も同一 context で行い、SaveChanges は呼ばない。
    public async Task<ServerLoan> GetOrCreateForUpdateAsync(BankDbContext db, string uuid, string player)
    {
        var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, uuid);
        if (loan != null) return loan;

        if (string.IsNullOrWhiteSpace(player))
        {
            var resolved = await profileService.GetNameByUuidAsync(uuid);
            if (resolved == null)
                throw new ArgumentException("指定された UUID のプレイヤーが見つかりません。", nameof(uuid));
            player = resolved;
        }

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
        return loan;
    }

    public async Task<List<ServerLoan>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().ToListAsync();
    }

    public async Task<ServerLoan?> GetByUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    // トランザクション合成用のコアメソッド。
    // 呼び出し側が用意した db(=トランザクション内)で行ロック付きの取得/作成を行い、
    // 残債(BorrowAmount)を更新してログを同一 context に追加する。SaveChanges / Commit は呼び出し側。
    public async Task<ServerLoan> AdjustLoanCoreAsync(
        BankDbContext db, string uuid, string player, decimal delta, ServerLoanLogAction action)
    {
        var loan = await GetOrCreateForUpdateAsync(db, uuid, player);
        if (!string.IsNullOrWhiteSpace(player))
            loan.Player = player;

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

        AddLog(db, loan.Uuid, loan.Player, action, delta);
        return loan;
    }

    // 単一 DbContext・単一トランザクションで残債を更新する独立メソッド。
    public async Task<ServerLoan?> AdjustLoanAsync(string uuid, string player, decimal delta, ServerLoanLogAction action)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await AdjustLoanCoreAsync(db, uuid, player, delta, action);
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

    // 当日分の Interest ログが存在するか(日次利息の冪等判定に使用)。
    public async Task<bool> HasInterestLogOnDateAsync(string uuid, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var action = ServerLoanLogAction.Interest.ToString();

        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoanLogs
            .AsNoTracking()
            .AnyAsync(x => x.Uuid == uuid && x.Action == action && x.Date >= start && x.Date < end);
    }

    // 週次等の支払額を設定する
    public async Task<ServerLoan?> SetPaymentAmountAsync(string uuid, decimal paymentAmount)
    {
        if (paymentAmount < 0m) throw new ArgumentException("支払額は 0 以上で指定してください。", nameof(paymentAmount));

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await GetOrCreateForUpdateAsync(db, uuid, string.Empty);

        loan.PaymentAmount = paymentAmount;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }

    public async Task<ServerLoan?> SetBorrowAmountAsync(string uuid, string player, decimal borrowAmount, decimal paymentAmount)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, uuid);
        if (loan == null)
        {
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
        }
        var delta = borrowAmount - loan.BorrowAmount;

        loan.BorrowAmount = borrowAmount;
        loan.PaymentAmount = paymentAmount;
        AddLog(db, loan.Uuid, loan.Player, ServerLoanLogAction.SetBorrowAmount, delta);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return loan;
    }

    private static void AddLog(BankDbContext db, string uuid, string player, ServerLoanLogAction action, decimal amount)
    {
        var log = new ServerLoanLog
        {
            Uuid = uuid,
            Player = player,
            Action = action.ToString(),
            Amount = amount,
            // Date は DB 既定値
        };
        db.ServerLoanLogs.Add(log);
    }
}
