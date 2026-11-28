using Man10BankService.Data;
using Man10BankService.Models.Database;
using Microsoft.EntityFrameworkCore;

namespace Man10BankService.Repositories;

// 旧リボ(0.5.0 までのサーバーローン)のテーブル(server_loan_tbl / server_loan_log)の読み書き。
// 判断は LegacyServerLoanService が行い、ここは行の取得と更新だけを受け持つ。
// どのメソッドも呼び出し側の BankDbContext を使い、SaveChanges は呼ばない。
//
// 金額の比較や合計は、行を読んでからメモリ上で行う(テストで使う SQLite が decimal の比較を SQL に変換できないため)。
public static class LegacyServerLoanRepository
{
    // 旧リボの処理が server_loan_log.action に書く値。
    public enum LogAction
    {
        // 定期返済の成功。金額は返済額(負)。
        RepaySuccess,
        // 定期返済の失敗(残高不足)。金額は引き落とそうとした額(正)で、残債は変わらない。
        RepayFailure,
        // 手動返済。金額は返済額(負)。
        EarlyRepay,
        // 運営による残債の上書き。金額は前後の差額。
        SetBorrowAmount,
    }

    // uuid の旧リボのうち、残債が残っているもの。無ければ null。
    public static async Task<LegacyServerLoan?> FindActiveAsync(BankDbContext db, string uuid)
    {
        var rows = await db.LegacyServerLoans
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return rows.FirstOrDefault(x => x.BorrowAmount > 0m);
    }

    // uuids のうち 1 人でも旧リボの残債が残っているかどうか。
    public static async Task<bool> HasDebtAsync(BankDbContext db, IReadOnlyCollection<string> uuids)
    {
        var debts = await db.LegacyServerLoans
            .AsNoTracking()
            .Where(x => uuids.Contains(x.Uuid))
            .Select(x => x.BorrowAmount)
            .ToListAsync();
        return debts.Any(x => x > 0m);
    }

    // uuid の旧リボの残債。無ければ 0。
    public static async Task<decimal> GetDebtAsync(BankDbContext db, string uuid)
    {
        return (await FindActiveAsync(db, uuid))?.BorrowAmount ?? 0m;
    }

    // 残債が残っている旧リボの全員。定期返済で 1 件ずつ処理するために使う。
    public static async Task<List<LegacyServerLoan>> GetAllActiveAsync(BankDbContext db)
    {
        var rows = await db.LegacyServerLoans.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return rows.Where(x => x.BorrowAmount > 0m).ToList();
    }

    // 旧リボの残債を増減し、その操作をログに残す。
    // loan には、呼び出し側が行ロックして取得したもの(同じ db で追跡中のもの)を渡す。
    //  - delta: 残債の増減(返済は負)。ログの金額にもこの値を記録する。
    //    RepayFailure だけは「引き落とそうとした額」を渡す。記録するだけで、残債は変えない。
    public static void Adjust(BankDbContext db, LegacyServerLoan loan, decimal delta, LogAction action)
    {
        switch (action)
        {
            case LogAction.RepaySuccess:
            case LogAction.EarlyRepay:
                loan.BorrowAmount += delta;
                loan.LastPayDate = DateTime.UtcNow;
                break;

            case LogAction.SetBorrowAmount:
                loan.BorrowAmount += delta;
                break;

            case LogAction.RepayFailure:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        db.LegacyServerLoanLogs.Add(new LegacyServerLoanLog
        {
            Uuid = loan.Uuid,
            Player = loan.Player,
            Action = action.ToString(),
            Amount = delta,
            // Date は DB の既定値(CURRENT_TIMESTAMP)
        });
    }

    // uuid の旧リボのログ(新しい順)。0.5.0 までの記録も含む。
    public static async Task<List<LegacyServerLoanLog>> GetLogsAsync(BankDbContext db, string uuid, int limit, int offset)
    {
        return await db.LegacyServerLoanLogs
            .AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
