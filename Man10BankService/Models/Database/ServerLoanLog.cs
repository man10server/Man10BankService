using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class ServerLoanLog
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    [StringLength(16)]
    public required string Action { get; set; } // borrow/repay/interest
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    // 操作したプレイヤーの UUID。自動処理(利息・定期返済・延滞回収)や運営の調整は null。
    [StringLength(36)]
    public string? ActorUuid { get; set; }
    // 操作後の残債。監査用で、借入枠などの計算には使わない。この列が無かった頃の行は null。
    public decimal? DebtAfter { get; set; }
}
