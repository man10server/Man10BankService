using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

// 電子マネー(user_vault)の永続化を担う。BankRepository と同形で、
// 行ロック→残高更新→version++→vault_log 追加までのコアを提供する。
// vault_log には操作後残高/version・冪等キー・session を残し、監査と冪等応答に使う(VaultProvider 9.2)。
public class VaultRepository(IDbContextFactory<BankDbContext> factory)
{
    // 残高+version を取得する。口座が無ければ (0, 0)。
    public async Task<(decimal Balance, long Version)> GetBalanceAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var vault = await db.UserVaults
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid);
        return (vault?.Balance ?? 0m, vault?.Version ?? 0L);
    }

    public async Task<List<VaultLog>> GetLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await using var db = await factory.CreateDbContextAsync();
        return await db.VaultLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    // 冪等キーで既存ログを引く(重複送信の同一結果応答用。VaultProvider 8.2)。
    public static Task<VaultLog?> FindByOperationIdAsync(BankDbContext db, string operationId)
    {
        return db.VaultLogs.AsNoTracking().FirstOrDefaultAsync(x => x.OperationId == operationId);
    }

    // トランザクション合成用のコア。呼び出し側が用意した db(=トランザクション内)で
    // 行ロック→残高更新(+= delta)→version++→vault_log 追加までを行う。
    // SaveChanges / Commit は呼び出し側が一度だけ行う。残高更新とログを同一 tx に載せる。
    public static async Task<UserVault> ChangeBalanceCoreAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server,
        string source,
        string? sessionId,
        string? operationId)
    {
        var vault = await GetOrCreateForUpdateAsync(db, uuid, player);
        vault.Balance += delta;
        vault.Version += 1;
        await AddLogAsync(db, vault, delta, pluginName, note, displayNote, server, source, sessionId, operationId);
        return vault;
    }

    // 絶対値設定用のコア。target との差分を vault_log に記録する。
    // 既存口座で差分が無い場合は version/ログを変更せず changed=false を返す。
    public static async Task<(UserVault Vault, bool Changed)> SetBalanceCoreAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal target,
        string pluginName,
        string note,
        string displayNote,
        string server,
        string source,
        string? sessionId)
    {
        var vault = await GetOrCreateForUpdateAsync(db, uuid, player);
        var delta = target - vault.Balance;
        if (delta == 0m)
            return (vault, false);

        vault.Balance = target;
        vault.Version += 1;
        await AddLogAsync(db, vault, delta, pluginName, note, displayNote, server, source, sessionId, null);
        return (vault, true);
    }

    // 口座を行ロック付きで取得する。無ければ 0 残高で作成する。
    // Player 名は空でなければ最新値で更新する。
    public static async Task<UserVault> GetOrCreateForUpdateAsync(BankDbContext db, string uuid, string player)
    {
        var vault = await DbLockHelper.GetUserVaultForUpdateAsync(db, uuid);
        if (vault == null)
        {
            vault = new UserVault
            {
                Player = player,
                Uuid = uuid,
                Balance = 0m,
                Version = 0L
            };
            await db.UserVaults.AddAsync(vault);
        }
        else if (!string.IsNullOrWhiteSpace(player))
        {
            vault.Player = player;
        }

        return vault;
    }

    private static async Task AddLogAsync(
        BankDbContext db,
        UserVault vault,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server,
        string source,
        string? sessionId,
        string? operationId)
    {
        var log = new VaultLog
        {
            Uuid = vault.Uuid,
            Player = vault.Player,
            Amount = delta,
            PluginName = pluginName,
            Note = note,
            DisplayNote = displayNote,
            Server = server,
            Deposit = delta >= 0m,
            OperationId = operationId,
            Source = source,
            SessionId = sessionId,
            BalanceAfter = vault.Balance,
            VersionAfter = vault.Version
        };
        await db.VaultLogs.AddAsync(log);
    }
}
