using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 旧リボの操作の記録(server_loan_log)。0.5.0 までのログもここに残っている。
public class LegacyServerLoanLog
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    // 操作の種類。旧リボの処理が書くのは LegacyServerLoanRepository.LogAction の名前。
    [StringLength(16)]
    public required string Action { get; set; }
    // 残債の増減(返済は負)。RepayFailure だけは引き落とそうとした額(正)。
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}
