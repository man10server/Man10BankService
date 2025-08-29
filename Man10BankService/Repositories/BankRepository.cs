using Man10BankService.Data;
using Man10BankService.Models;
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

        // 口座の取得/作成
        var bank = await db.UserBanks
            .FirstOrDefaultAsync(x => x.Uuid == uuid);

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
            // Player 名が渡された場合は最新値で更新（空文字は無視）
            if (!string.IsNullOrWhiteSpace(player))
                bank.Player = player;
        }

        // 残高更新
        bank.Balance += delta;
        await db.SaveChangesAsync();

        // MoneyLog 追加
        await AddMoneyLogAsync(
            uuid: uuid,
            player: player,
            amount: delta,
            pluginName: pluginName,
            note: note,
            displayNote: displayNote,
            server: server,
            deposit: delta >= 0m);

        await tx.CommitAsync();
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

    private async Task AddMoneyLogAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName,
        string note,
        string displayNote,
        string server,
        bool deposit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var log = new MoneyLog
        {
            Uuid = uuid,
            Player = player,
            Amount = amount,
            PluginName = pluginName,
            Note = note,
            DisplayNote = displayNote,
            Server = server,
            Deposit = deposit,
            // Date は DB 既定値（CURRENT_TIMESTAMP）を使用
        };

        await db.MoneyLogs.AddAsync(log);
        await db.SaveChangesAsync();
    }
}
