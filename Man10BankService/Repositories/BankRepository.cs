using Man10BankService.Data;
using Man10BankService.Models;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class BankRepository
{
    private readonly BankDbContext _db;

    public BankRepository(BankDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetBalanceAsync(string uuid)
    {
        var bank = await _db.UserBanks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid);
        return bank?.Balance ?? 0m;
    }

    public async Task<List<MoneyLog>> GetMoneyLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);
        return await _db.MoneyLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<decimal> ChangeBalanceAsync(
        string uuid,
        string player,
        decimal delta,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "")
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        // 口座の取得/作成
        var bank = await _db.UserBanks
            .FirstOrDefaultAsync(x => x.Uuid == uuid);

        if (bank == null)
        {
            bank = new UserBank
            {
                Player = player,
                Uuid = uuid,
                Balance = 0m
            };
            await _db.UserBanks.AddAsync(bank);
        }
        else
        {
            // Player 名が渡された場合は最新値で更新（空文字は無視）
            if (!string.IsNullOrWhiteSpace(player))
                bank.Player = player;
        }

        // 残高更新
        bank.Balance += delta;
        await _db.SaveChangesAsync();

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

        await _db.MoneyLogs.AddAsync(log);
        await _db.SaveChangesAsync();
    }
}
