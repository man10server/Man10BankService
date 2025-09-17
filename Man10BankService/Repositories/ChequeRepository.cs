using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

public class ChequeRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<Cheque> CreateChequeAsync(string uuid, string player, decimal amount, string note, bool op)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cheque = new Cheque
        {
            Uuid = uuid,
            Player = player,
            Amount = amount,
            Note = note,
            Used = false,
            Op = op,
        };
        await db.Cheques.AddAsync(cheque);
        await db.SaveChangesAsync();
        return cheque;
    }

    public async Task<Cheque?> GetChequeAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Cheques.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<Cheque?> UseChequeAsync(int id, string usePlayer)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var cheque = await db.Cheques.FirstOrDefaultAsync(x => x.Id == id);
        if (cheque == null)
            return null;
        if (cheque.Used)
            return cheque; // 呼び出し側で既使用を解釈

        cheque.Used = true;
        cheque.UsePlayer = usePlayer;
        cheque.UseDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return cheque;
    }
}
