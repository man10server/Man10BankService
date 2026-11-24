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
        EarlyRepay,
        OverdueCollect,
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
    // 既にロック取得済みの loan エンティティ(同一 context にアタッチ済み)に対して
    // 残債(BorrowAmount)を更新し、ログを同一 context に追加する。SaveChanges / Commit は呼び出し側。
    public static void AdjustLoanCore(
        BankDbContext db, ServerLoan loan, string player, decimal delta, ServerLoanLogAction action)
    {
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
            case ServerLoanLogAction.EarlyRepay:
            case ServerLoanLogAction.OverdueCollect:
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
    }

    // 単一 DbContext・単一トランザクションで残債を更新する独立メソッド。
    public async Task<ServerLoan?> AdjustLoanAsync(string uuid, string player, decimal delta, ServerLoanLogAction action)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var loan = await GetOrCreateForUpdateAsync(db, uuid, player);
        AdjustLoanCore(db, loan, player, delta, action);
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

    // 借入枠の算定に使う、現在の残債と全ログ(古い順)を取得する。ローンが無いプレイヤーの残債は 0 で、行は作らない。
    public async Task<(decimal Debt, List<ServerLoanLog> Logs)> GetBorrowLimitSourceAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var debt = await GetDebtAsync(db, uuid) ?? 0m;
        var logs = await db.ServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderBy(x => x.Date).ThenBy(x => x.Id)
            .ToListAsync();
        return (debt, logs);
    }

    // 直近の定期返済(週次)が失敗かどうか。早期返済は判定に含めない。
    public async Task<bool> IsLatestScheduledRepayFailureAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await IsLatestScheduledRepayFailureAsync(db, uuid);
    }

    public static async Task<bool> IsLatestScheduledRepayFailureAsync(BankDbContext db, string uuid)
    {
        var latest = await GetLatestScheduledRepayLogAsync(db, uuid);
        return latest?.Action == ServerLoanLogAction.RepayFailure.ToString();
    }

    // 残債があり、かつ延滞中(直近の定期返済が失敗のまま)かどうか。
    // 延滞中は銀行からの出金・送金・小切手作成・貸付を拒否するために使う。
    public static async Task<bool> HasOverdueDebtAsync(BankDbContext db, string uuid)
    {
        var debt = await GetDebtAsync(db, uuid);
        if (debt is null or <= 0m)
            return false;

        return await IsLatestScheduledRepayFailureAsync(db, uuid);
    }

    // 現在の残債。ローンが無いプレイヤーは null で、行は作らない。
    private static async Task<decimal?> GetDebtAsync(BankDbContext db, string uuid)
    {
        return await db.ServerLoans
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .Select(x => (decimal?)x.BorrowAmount)
            .FirstOrDefaultAsync();
    }

    // 延滞分(直近の定期返済の失敗額)のうち未回収の額。延滞中でなければ null。
    public static async Task<decimal?> GetUncollectedOverdueAmountAsync(BankDbContext db, string uuid)
    {
        var latest = await GetLatestScheduledRepayLogAsync(db, uuid);
        if (latest?.Action != ServerLoanLogAction.RepayFailure.ToString())
            return null;

        var collect = ServerLoanLogAction.OverdueCollect.ToString();
        var collected = await db.ServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid && x.Action == collect && x.Id > latest.Id)
            .Select(x => x.Amount)
            .ToListAsync();

        // 回収ログの金額は残債の増減(負数)で記録される。
        return Math.Max(0m, latest.Amount + collected.Sum());
    }

    private static async Task<ServerLoanLog?> GetLatestScheduledRepayLogAsync(BankDbContext db, string uuid)
    {
        var success = ServerLoanLogAction.RepaySuccess.ToString();
        var failure = ServerLoanLogAction.RepayFailure.ToString();

        return await db.ServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid && (x.Action == success || x.Action == failure))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
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
