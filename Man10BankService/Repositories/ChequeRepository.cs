using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ChequeRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<Cheque?> GetChequeAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Cheques.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    // トランザクション合成用: 呼び出し側の db(=tx内)に小切手を追加する。SaveChanges は呼び出し側。
    public static Cheque AddChequeCore(BankDbContext db, string uuid, string player, decimal amount, string note, bool op)
    {
        var cheque = new Cheque
        {
            Uuid = uuid,
            Player = player,
            Amount = amount,
            Note = note,
            Used = false,
            Op = op,
        };
        db.Cheques.Add(cheque);
        return cheque;
    }

    // トランザクション合成用: 呼び出し側の db(=tx内)で小切手を FOR UPDATE で取得する(MySQL時)。
    public static Task<Cheque?> GetChequeForUpdateAsync(BankDbContext db, int id)
        => DbLockHelper.GetChequeForUpdateAsync(db, id);
}
