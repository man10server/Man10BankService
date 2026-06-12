using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

// DESIGN 2.2: 全リポジトリを IDbContextFactory 注入へ統一。
// トランザクション合成が必要なメソッド(FOR UPDATE 取得等)は外部から DbContext を受け取る static にする。
public class LoanRepository(IDbContextFactory<BankDbContext> factory)
{
    public async Task<Loan?> GetByIdAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    // トランザクション合成用: 呼び出し側の db(=tx内)で FOR UPDATE 取得する(MySQL時)。
    public static Task<Loan?> GetByIdForUpdateAsync(BankDbContext db, int id)
        => DbLockHelper.GetLoanForUpdateAsync(db, id);

    public async Task<List<Loan>> GetByBorrowerUuidAsync(string borrowUuid, int limit, int offset)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Loans
            .AsNoTracking()
            .Where(x => x.BorrowUuid == borrowUuid)
            .OrderByDescending(x => x.BorrowDate).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
