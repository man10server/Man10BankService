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
        // 他のプレイヤーの契約への参加(参加先の契約に記録。金額は移ってきた残債)と脱退
        MemberJoin,
        MemberLeave,
        // 借入枠を初期枠(MinAmount)に戻す(運営による強制リンク時の罰則)
        LimitReset,
    }

    // uuid が使う契約の本垢を返す。承認済みで他のプレイヤーの契約に参加していればその本垢、していなければ自分自身。
    public async Task<string> ResolveContractUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await ResolveContractUuidAsync(db, uuid);
    }

    public static async Task<string> ResolveContractUuidAsync(BankDbContext db, string uuid)
    {
        var ownerUuid = await db.ServerLoanMembers
            .AsNoTracking()
            .Where(x => x.Uuid == uuid && x.ApprovedAt != null)
            .Select(x => x.OwnerUuid)
            .FirstOrDefaultAsync();
        return ownerUuid ?? uuid;
    }

    // 契約の定期返済・延滞回収で引き落とすアカウント。本垢を先頭に、承認済みのメンバーを承認が古い順に並べる。
    public static async Task<List<string>> GetContractAccountUuidsAsync(BankDbContext db, string ownerUuid)
    {
        var members = await db.ServerLoanMembers
            .AsNoTracking()
            .Where(x => x.OwnerUuid == ownerUuid && x.ApprovedAt != null)
            .OrderBy(x => x.ApprovedAt).ThenBy(x => x.Uuid)
            .Select(x => x.Uuid)
            .ToListAsync();
        return [ownerUuid, .. members];
    }

    // 契約に承認済みの参加者がいるかどうか。
    public async Task<bool> HasMembersAsync(string ownerUuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await HasMembersAsync(db, ownerUuid);
    }

    public static Task<bool> HasMembersAsync(BankDbContext db, string ownerUuid)
    {
        return db.ServerLoanMembers.AnyAsync(x => x.OwnerUuid == ownerUuid && x.ApprovedAt != null);
    }

    public async Task<ServerLoan> GetOrCreateByUuidAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var loan = await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
        if (loan != null) return loan;

        loan = await NewContractAsync(uuid);
        await db.ServerLoans.AddAsync(loan);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // 同時に作成された場合は一意制約で弾かれるので、先に作られた行を返す。
            await using var retry = await factory.CreateDbContextAsync();
            var existing = await retry.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
            if (existing == null) throw;
            return existing;
        }

        return loan;
    }

    // トランザクション内で契約の行を取得する。無ければ残債 0 の行を作る。
    // 既存行は FOR UPDATE で行ロックする(MySQL 時)。作成も同一 context で行い、SaveChanges は呼ばない。
    public async Task<ServerLoan> GetOrCreateForUpdateAsync(BankDbContext db, string uuid)
    {
        var loan = await DbLockHelper.GetServerLoanForUpdateAsync(db, uuid);
        if (loan != null) return loan;

        loan = await NewContractAsync(uuid);
        await db.ServerLoans.AddAsync(loan);
        return loan;
    }

    // 残債 0 の新しい契約。本垢の名前は名簿から引く。名簿に無いプレイヤーの契約は作れない。
    private async Task<ServerLoan> NewContractAsync(string uuid)
    {
        var player = await profileService.GetNameByUuidAsync(uuid);
        if (player == null)
            throw new ArgumentException("指定された UUID のプレイヤーが見つかりません。", nameof(uuid));

        return new ServerLoan
        {
            Uuid = uuid,
            Player = player,
            BorrowAmount = 0m,
            PaymentAmount = 0m,
            LastPayDate = DateTime.UtcNow,
            StopInterest = false,
        };
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
    // actorUuid には操作したプレイヤーを渡す(自動処理は null)。
    public static void AdjustLoanCore(
        BankDbContext db, ServerLoan loan, decimal delta, ServerLoanLogAction action,
        string? actorUuid = null)
    {
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
                loan.BorrowAmount += delta;
                break;
            case ServerLoanLogAction.RepaySuccess:
            case ServerLoanLogAction.EarlyRepay:
            case ServerLoanLogAction.OverdueCollect:
                loan.BorrowAmount += delta;
                loan.LastPayDate = DateTime.UtcNow;
                break;
            case ServerLoanLogAction.RepayFailure:
                // 残債は変えず、失敗のログだけを残す。
                break;
            case ServerLoanLogAction.SetBorrowAmount:
            case ServerLoanLogAction.MemberJoin:
                loan.BorrowAmount += delta;
                break;
            case ServerLoanLogAction.MemberLeave:
            case ServerLoanLogAction.LimitReset:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        AddLog(db, loan, action, delta, actorUuid);
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

    // uuid が使う契約に残債があり、かつ延滞中(直近の定期返済が失敗のまま)かどうか。
    // 延滞中は銀行からの出金・送金・小切手作成・貸付を拒否するために使う。
    // 他のプレイヤーの契約に参加しているアカウントは、参加先の契約で判定する。
    public static async Task<bool> HasOverdueDebtAsync(BankDbContext db, string uuid)
    {
        var contractUuid = await ResolveContractUuidAsync(db, uuid);
        var debt = await GetDebtAsync(db, contractUuid);
        if (debt is null or <= 0m)
            return false;

        return await IsLatestScheduledRepayFailureAsync(db, contractUuid);
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
    public static async Task<bool> HasInterestLogOnDateAsync(BankDbContext db, string uuid, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var action = ServerLoanLogAction.Interest.ToString();

        return await db.ServerLoanLogs
            .AsNoTracking()
            .AnyAsync(x => x.Uuid == uuid && x.Action == action && x.Date >= start && x.Date < end);
    }

    // 残債を更新した後の loan を渡す(操作後の残債を記録する)。
    private static void AddLog(BankDbContext db, ServerLoan loan, ServerLoanLogAction action, decimal amount, string? actorUuid)
    {
        var log = new ServerLoanLog
        {
            Uuid = loan.Uuid,
            Player = loan.Player,
            Action = action.ToString(),
            Amount = amount,
            ActorUuid = actorUuid,
            DebtAfter = loan.BorrowAmount,
            // Date は DB 既定値
        };
        db.ServerLoanLogs.Add(log);
    }
}
