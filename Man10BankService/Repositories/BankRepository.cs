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

    // public: 所持金取得（UUID指定）
    public async Task<decimal> GetBalanceAsync(string uuid, CancellationToken ct = default)
    {
        var bank = await _db.UserBanks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid, ct);
        return bank?.Balance ?? 0m;
    }

    // public: MoneyLog 取得（UUID指定、簡易ページング）
    public async Task<List<MoneyLog>> GetMoneyLogsAsync(string uuid, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);
        return await _db.MoneyLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    // public: 所持金の増減（UUID指定）。増減後の残高を返す。
    // delta > 0 で入金（Deposit=true）、delta < 0 で出金（Deposit=false）
    public async Task<decimal> ChangeBalanceAsync(
        string uuid,
        string player,
        decimal delta,
        string pluginName = "api",
        string note = "",
        string displayNote = "",
        string server = "",
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 口座の取得/作成
        var bank = await _db.UserBanks
            .FirstOrDefaultAsync(x => x.Uuid == uuid, ct);

        if (bank == null)
        {
            bank = new UserBank
            {
                Player = player,
                Uuid = uuid,
                Balance = 0m
            };
            await _db.UserBanks.AddAsync(bank, ct);
        }
        else
        {
            // Player 名が渡された場合は最新値で更新（空文字は無視）
            if (!string.IsNullOrWhiteSpace(player))
                bank.Player = player;
        }

        // 残高更新
        bank.Balance += delta;
        await _db.SaveChangesAsync(ct);

        // MoneyLog 追加
        await AddMoneyLogAsync(
            uuid: uuid,
            player: player,
            amount: delta,
            pluginName: pluginName,
            note: note,
            displayNote: displayNote,
            server: server,
            deposit: delta >= 0m,
            ct: ct);

        await tx.CommitAsync(ct);
        return bank.Balance;
    }

    // private: MoneyLog 追加
    private async Task AddMoneyLogAsync(
        string uuid,
        string player,
        decimal amount,
        string pluginName,
        string note,
        string displayNote,
        string server,
        bool deposit,
        CancellationToken ct)
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

        await _db.MoneyLogs.AddAsync(log, ct);
        await _db.SaveChangesAsync(ct);
    }
}

