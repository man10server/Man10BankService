using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

// サーバーローンのテーブル(server_loan_contract / server_loan_contract_log / server_loan_contract_member)の読み書き。
// 判断(借りられるか、いくら引き落とすか等)は ServerLoanService が行い、ここは行の取得と更新だけを受け持つ。
//  - BankDbContext を受け取る static メソッド: 呼び出し側のトランザクションの中で使う。SaveChanges は呼ばない。
//  - それ以外のメソッド: 自分で接続を開いて読む、参照用。
public class ServerLoanRepository(IDbContextFactory<BankDbContext> factory, IPlayerProfileService profileService)
{
    // server_loan_contract_log.action の値。ログの金額(amount)は残債の増減で、種類ごとの意味は次のとおり。
    public enum ServerLoanLogAction
    {
        // 借入。金額は借入額(正)。
        Borrow,
        // 定期返済の成功。金額は返済額(負)。0 は、返済を伴わない延滞の解消を表す。
        RepaySuccess,
        // 定期返済の失敗(残高不足)。金額は引き落とそうとした額(正)で、残債は変わらない。
        RepayFailure,
        // 日次の利息。金額は利息額(正)。
        Interest,
        // 運営による残債の上書きと、契約への参加で自分の契約の残債を移したとき。金額は前後の差額。
        SetBorrowAmount,
        // 手動返済。金額は返済額(負)。
        EarlyRepay,
        // 延滞分の回収。金額は回収額(負)。
        OverdueCollect,
        // 他のプレイヤーの契約への参加(参加先の契約に記録)。金額は移ってきた残債(正。無ければ 0)。
        MemberJoin,
        // 契約からの脱退(参加していた契約に記録)。金額は 0。
        MemberLeave,
        // 借入枠を初期枠(MinAmount)に戻す(運営による強制リンク時の罰則)。金額は 0。
        LimitReset,
    }

    // ===== どの契約を使うか(契約の共有) =====

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

    // ===== 契約の行(server_loan_contract) =====

    // 契約の行を返す(参照用)。無ければ残債 0 の行を作って保存する。
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

    // 全契約。スケジューラが 1 件ずつ処理するために使う。
    public async Task<List<ServerLoan>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().ToListAsync();
    }

    // ===== 残債の変更とログ =====

    // 契約の残債を増減し、その操作をログに残す。残債を変える処理は必ずここを通す。
    // loan には、呼び出し側が行ロックして取得したもの(同じ db で追跡中のもの)を渡す。SaveChanges / Commit は呼び出し側。
    //  - delta: 残債の増減(借入・利息は正、返済は負)。ログの金額にもこの値を記録する。
    //    RepayFailure だけは「引き落とそうとした額」を渡す。記録するだけで、残債は変えない。
    //  - actorUuid: 操作したプレイヤー。自動処理(利息・定期返済・延滞分の回収)と運営の調整は null。
    public static void AdjustLoanCore(
        BankDbContext db, ServerLoan loan, decimal delta, ServerLoanLogAction action,
        string? actorUuid = null)
    {
        var now = DateTime.UtcNow;
        switch (action)
        {
            case ServerLoanLogAction.Borrow:
                // 残債 0 から借り始めるときは、前回返済日(last_pay_date)を今に置き直す。
                if (loan.BorrowAmount == 0m)
                    loan.LastPayDate = now;
                loan.BorrowAmount += delta;
                break;

            case ServerLoanLogAction.Interest:
            case ServerLoanLogAction.SetBorrowAmount:
            case ServerLoanLogAction.MemberJoin:
                loan.BorrowAmount += delta;
                break;

            // 返済は、前回返済日も更新する。
            case ServerLoanLogAction.RepaySuccess:
            case ServerLoanLogAction.EarlyRepay:
            case ServerLoanLogAction.OverdueCollect:
                loan.BorrowAmount += delta;
                loan.LastPayDate = now;
                break;

            // ログを残すだけで、残債は変えない。
            case ServerLoanLogAction.RepayFailure:
            case ServerLoanLogAction.MemberLeave:
            case ServerLoanLogAction.LimitReset:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        // 残債を更新した後に記録する(操作後の残債 debt_after を残すため)。
        db.ServerLoanLogs.Add(new ServerLoanLog
        {
            Uuid = loan.Uuid,
            Player = loan.Player,
            Action = action.ToString(),
            Amount = delta,
            ActorUuid = actorUuid,
            DebtAfter = loan.BorrowAmount,
            // Date は DB の既定値(CURRENT_TIMESTAMP)
        });
    }

    // ===== ログの参照(server_loan_contract_log) =====

    // 契約のログ(新しい順)。limit / offset の範囲は呼び出し側(ServerLoanService)が確かめる。
    public async Task<List<ServerLoanLog>> GetLogsAsync(string uuid, int limit, int offset)
    {
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
        return (debt, await GetLogsOldestFirstAsync(db, uuid));
    }

    // 契約の全ログ(古い順)。借入枠はこの並びを先頭からたどって求める。
    public static async Task<List<ServerLoanLog>> GetLogsOldestFirstAsync(BankDbContext db, string uuid)
    {
        return await db.ServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderBy(x => x.Date).ThenBy(x => x.Id)
            .ToListAsync();
    }

    // ===== 延滞の判定 =====
    // 延滞は「直近の定期返済のログ(RepaySuccess / RepayFailure)が失敗である」状態。手動返済や延滞分の回収のログは見ない。

    // 直近の定期返済が失敗かどうか(= 延滞中かどうか)。
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

    // 直近の定期返済のログ(成功または失敗)。一度も定期返済の対象になっていなければ null。
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

    // date(UTC)の分の Interest ログがあるかどうか。同じ日に利息を二重に付けないための判定に使う。
    public static async Task<bool> HasInterestLogOnDateAsync(BankDbContext db, string uuid, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var action = ServerLoanLogAction.Interest.ToString();

        return await db.ServerLoanLogs
            .AsNoTracking()
            .AnyAsync(x => x.Uuid == uuid && x.Action == action && x.Date >= start && x.Date < end);
    }
}
