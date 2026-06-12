using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class BankRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<decimal> GetBalanceAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bank = await db.UserBanks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid);
        return bank?.Balance ?? 0m;
    }

    // 単一 DbContext・単一トランザクションで残高変更を行う。
    // 行ロック→残高更新→MoneyLog 追加→SaveChanges→Commit をこのメソッド内で完結する。
    public async Task<decimal> ChangeBalanceAsync(
        string uuid,
        string player,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var balance = await ChangeBalanceCoreAsync(db, uuid, player, delta, pluginName, note, displayNote, server);

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return balance;
    }

    // トランザクション合成用のコアメソッド。
    // 呼び出し側が用意した db(=トランザクション内)で行ロック→残高更新→MoneyLog 追加までを行う。
    // SaveChanges / Commit は呼び出し側が一度だけ行う。残高更新と MoneyLog を同一 tx に載せることで
    // 監査ログと実残高の乖離を防ぐ。
    public static async Task<decimal> ChangeBalanceCoreAsync(
        BankDbContext db,
        string uuid,
        string player,
        decimal delta,
        string pluginName,
        string note,
        string displayNote,
        string server)
    {
        // 口座の取得(MySQL 時は FOR UPDATE で行ロック)/作成
        var bank = await DbLockHelper.GetUserBankForUpdateAsync(db, uuid);

        if (bank == null)
        {
            bank = new UserBank
            {
                Player = player,
                Uuid = uuid,
                Balance = 0m
            };
            await db.UserBanks.AddAsync(bank);
        }
        else
        {
            // Player 名が渡された場合は最新値で更新(空文字は無視)
            if (!string.IsNullOrWhiteSpace(player))
                bank.Player = player;
        }

        // 残高更新
        bank.Balance += delta;

        // MoneyLog を同一 context に追加(Date は DB 既定値 CURRENT_TIMESTAMP)
        var log = new MoneyLog
        {
            Uuid = uuid,
            Player = player,
            Amount = delta,
            PluginName = pluginName,
            Note = note,
            DisplayNote = displayNote,
            Server = server,
            Deposit = delta >= 0m,
        };
        await db.MoneyLogs.AddAsync(log);

        return bank.Balance;
    }

    public async Task<List<MoneyLog>> GetMoneyLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await using var db = await factory.CreateDbContextAsync();
        return await db.MoneyLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
