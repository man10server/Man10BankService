using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Data;

// 行ロックの共通ヘルパ。
// MySQL では SELECT ... FOR UPDATE で悲観ロックを取得し、SQLite(テスト)では
// FOR UPDATE をサポートしないため通常の SELECT にフォールバックする。
// プロバイダ判定を1箇所に集約し、リポジトリ間でロック取得方法を統一する。
public static class DbLockHelper
{
    // 現在のコンテキストが MySQL プロバイダかどうかを判定する。
    public static bool IsMySql(BankDbContext db)
    {
        var provider = db.Database.ProviderName;
        return provider != null && provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
    }

    // user_bank の行を UUID で取得する。MySQL 時は FOR UPDATE で行ロックする。
    // 該当行が無ければ null。
    public static async Task<Models.Database.UserBank?> GetUserBankForUpdateAsync(BankDbContext db, string uuid)
    {
        if (IsMySql(db))
        {
            return await db.UserBanks
                .FromSqlInterpolated($"SELECT * FROM user_bank WHERE uuid = {uuid} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        return await db.UserBanks.FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    // user_vault の行を UUID で取得する。MySQL 時は FOR UPDATE で行ロックする。
    // 該当行が無ければ null。
    public static async Task<Models.Database.UserVault?> GetUserVaultForUpdateAsync(BankDbContext db, string uuid)
    {
        if (IsMySql(db))
        {
            return await db.UserVaults
                .FromSqlInterpolated($"SELECT * FROM user_vault WHERE uuid = {uuid} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        return await db.UserVaults.FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    // server_loan の行を UUID で取得する。MySQL 時は FOR UPDATE で行ロックする。
    // 該当行が無ければ null。
    public static async Task<Models.Database.ServerLoan?> GetServerLoanForUpdateAsync(BankDbContext db, string uuid)
    {
        if (IsMySql(db))
        {
            return await db.ServerLoans
                .FromSqlInterpolated($"SELECT * FROM server_loan_tbl WHERE uuid = {uuid} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        return await db.ServerLoans.FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    // cheque を ID で取得する。MySQL 時は FOR UPDATE で行ロックする。
    // 該当行が無ければ null。
    public static async Task<Models.Database.Cheque?> GetChequeForUpdateAsync(BankDbContext db, int id)
    {
        if (IsMySql(db))
        {
            return await db.Cheques
                .FromSqlInterpolated($"SELECT * FROM cheque_tbl WHERE id = {id} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        return await db.Cheques.FirstOrDefaultAsync(x => x.Id == id);
    }

    // loan を ID で取得する。MySQL 時は FOR UPDATE で行ロックする。
    // 該当行が無ければ null。
    public static async Task<Models.Database.Loan?> GetLoanForUpdateAsync(BankDbContext db, int id)
    {
        if (IsMySql(db))
        {
            return await db.Loans
                .FromSqlInterpolated($"SELECT * FROM loan_table WHERE id = {id} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        return await db.Loans.FirstOrDefaultAsync(x => x.Id == id);
    }
}
