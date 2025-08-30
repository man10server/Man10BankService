using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class AtmRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<AtmLog> AddAtmLogAsync(string uuid, string player, decimal amount, bool deposit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var log = new AtmLog
        {
            Uuid = uuid,
            Player = player,
            Amount = amount,
            Deposit = deposit,
            // Date は DB 既定値を使用
        };
        await db.AtmLogs.AddAsync(log);
        await db.SaveChangesAsync();
        return log;
    }

    public async Task<List<AtmLog>> GetAtmLogsAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await using var db = await factory.CreateDbContextAsync();
        return await db.AtmLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}

