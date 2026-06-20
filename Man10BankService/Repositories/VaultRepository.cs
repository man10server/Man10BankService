using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

// 電子マネー(user_vault)の残高更新コア。BankRepository と同形。
// 残高更新は必ず version++ と vault_log 追加を同一 transaction に載せ、監査ログと実残高の乖離を防ぐ。
public class VaultRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<VaultBalanceData> GetBalanceAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var vault = await db.UserVaults
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid);
        return new VaultBalanceData(vault?.Balance ?? 0m, vault?.Version ?? 0L);
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

    // operation_id で既存の vault_log を探す(冪等再送の判定用)。指定がなければ常に null。
    public static async Task<VaultLog?> FindLogByOperationIdAsync(BankDbContext db, string? operationId)
    {
        if (string.IsNullOrEmpty(operationId)) return null;
        return await db.VaultLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId);
    }

    // user_vault の行を取得(MySQL 時は FOR UPDATE)。無ければ作成して context に追加する。
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

    // トランザクション合成用コア。行ロック→残高更新→version++→vault_log 追加までを行う。
    // SaveChanges / Commit は呼び出し側が一度だけ行う。返り値は更新後の残高・版数。
    public static async Task<VaultBalanceData> ChangeBalanceCoreAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server,
        VaultSource source,
        string? operationId)
    {
        var vault = await GetOrCreateForUpdateAsync(db, uuid, player);

        vault.Balance += delta;
        vault.Version += 1;

        await AddLogAsync(db, uuid, player, delta, pluginName, note, displayNote, server, source, operationId, vault.Balance);

        return new VaultBalanceData(vault.Balance, vault.Version);
    }

    // 絶対値設定用コア。現在残高との差分をログに残し、version++ する。差分 0 でも version は進める
    // (管理者操作の監査痕跡を残すため)。
    public static async Task<VaultBalanceData> SetBalanceCoreAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal absolute,
        string pluginName,
        string note,
        string displayNote,
        string server,
        VaultSource source,
        string? operationId)
    {
        var vault = await GetOrCreateForUpdateAsync(db, uuid, player);

        var delta = absolute - vault.Balance;
        vault.Balance = absolute;
        vault.Version += 1;

        await AddLogAsync(db, uuid, player, delta, pluginName, note, displayNote, server, source, operationId, vault.Balance);

        return new VaultBalanceData(vault.Balance, vault.Version);
    }

    private static async Task AddLogAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server,
        VaultSource source,
        string? operationId,
        decimal balanceAfter)
    {
        var log = new VaultLog
        {
            Uuid = uuid,
            Player = player,
            Amount = delta,
            PluginName = pluginName,
            Note = note,
            DisplayNote = displayNote,
            Server = server,
            Deposit = delta >= 0m,
            Source = source,
            OperationId = string.IsNullOrEmpty(operationId) ? null : operationId,
            BalanceAfter = balanceAfter
        };
        await db.VaultLogs.AddAsync(log);
    }
}
