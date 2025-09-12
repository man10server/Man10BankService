using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ServerEstateRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<ServerEstateHistory> RecordSnapshotAsync(DateTime? hourUtc = null)
    {
        var nowUtc = hourUtc ?? DateTime.UtcNow;
        var ts = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);

        await using var db = await factory.CreateDbContextAsync();

        var sums = await db.Estates
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Vault = g.Sum(x => x.Vault),
                Bank = g.Sum(x => x.Bank),
                Cash = g.Sum(x => x.Cash),
                EstateAmount = g.Sum(x => x.EstateAmount),
                Loan = g.Sum(x => x.Loan),
                Shop = g.Sum(x => x.Shop),
                Crypto = g.Sum(x => x.Crypto),
                Total = g.Sum(x => x.Total)
            })
            .FirstOrDefaultAsync();

        var entity = new ServerEstateHistory
        {
            Vault = sums?.Vault ?? 0m,
            Bank = sums?.Bank ?? 0m,
            Cash = sums?.Cash ?? 0m,
            EstateAmount = sums?.EstateAmount ?? 0m,
            Loan = sums?.Loan ?? 0m,
            Shop = sums?.Shop ?? 0m,
            Crypto = sums?.Crypto ?? 0m,
            Total = sums?.Total ?? 0m,
            Year = ts.Year,
            Month = ts.Month,
            Day = ts.Day,
            Hour = ts.Hour,
            Date = ts,
        };

        await db.ServerEstateHistories.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }
}

