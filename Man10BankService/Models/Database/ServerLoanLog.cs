using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// サーバーローンの操作の記録(server_loan_contract_log)。借入枠と延滞の状態は、このログから求める。
public class ServerLoanLog
{
    public int Id { get; set; }
    // 契約の本垢の名前と UUID。参加者が操作した場合も、本垢の契約のログとして記録する。
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    // 操作の種類(ServerLoanRepository.ServerLoanLogAction の名前)。
    [StringLength(16)]
    public required string Action { get; set; }
    // 残債の増減(借入・利息は正、返済は負)。種類ごとの意味は ServerLoanLogAction を参照。
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    // 操作したプレイヤーの UUID。自動処理(利息・定期返済・延滞回収)や運営の調整は null。
    [StringLength(36)]
    public string? ActorUuid { get; set; }
    // 操作後の残債。監査用で、借入枠などの計算には使わない。
    public decimal DebtAfter { get; set; }
}
