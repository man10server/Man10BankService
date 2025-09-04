using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class EstateRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<Estate?> GetLatestAsync(string uuid)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Estates.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    public async Task<List<EstateHistory>> GetHistoryAsync(string uuid, int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        if (offset < 0) offset = 0;

        await using var db = await factory.CreateDbContextAsync();
        return await db.EstateHistories
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// 新しいスナップショットが現行と重複していない場合に Estate を更新し、EstateHistory に同一値を記録します。
    /// 重複（全フィールド一致）の場合は何もしません。
    /// </summary>
    /// <returns>更新が行われたかどうかのフラグ</returns>
    public async Task<bool> AddSnapshotIfChangedAsync(
        string uuid,
        string player,
        decimal vault,
        decimal bank,
        decimal cash,
        decimal estateAmount,
        decimal loan,
        decimal shop,
        decimal crypto,
        decimal total)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var current = await db.Estates.FirstOrDefaultAsync(x => x.Uuid == uuid);

        var isDifferent = current == null ||
                          current.Player != player ||
                          current.Vault != vault ||
                          current.Bank != bank ||
                          current.Cash != cash ||
                          current.EstateAmount != estateAmount ||
                          current.Loan != loan ||
                          current.Shop != shop ||
                          current.Crypto != crypto ||
                          current.Total != total;

        if (!isDifferent)
        {
            await tx.RollbackAsync();
            return false;
        }

        // 更新 or 新規作成
        if (current == null)
        {
            current = new Estate
            {
                Uuid = uuid,
                Player = player,
                Vault = vault,
                Bank = bank,
                Cash = cash,
                EstateAmount = estateAmount,
                Loan = loan,
                Shop = shop,
                Crypto = crypto,
                Total = total,
                Date = DateTime.UtcNow,
            };
            await db.Estates.AddAsync(current);
        }
        else
        {
            current.Player = player;
            current.Vault = vault;
            current.Bank = bank;
            current.Cash = cash;
            current.EstateAmount = estateAmount;
            current.Loan = loan;
            current.Shop = shop;
            current.Crypto = crypto;
            current.Total = total;
            current.Date = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // History へ同一値で記録
        var hist = new EstateHistory
        {
            Uuid = uuid,
            Player = player,
            Vault = vault,
            Bank = bank,
            Cash = cash,
            EstateAmount = estateAmount,
            Loan = loan,
            Shop = shop,
            Crypto = crypto,
            Total = total,
            // Date は DB 既定値 or 現在時刻
            Date = DateTime.UtcNow,
        };
        await db.EstateHistories.AddAsync(hist);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
        return true;
    }
}
