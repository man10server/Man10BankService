using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 旧リボ(0.5.0 までのサーバーローン)の残債(server_loan_tbl)。1 プレイヤーにつき 1 行。
// 残債(borrow_amount)が 1 円以上残っている行だけを「旧リボがある」として扱う。
// 旧リボは利息が付かず、新しく借りることもできない。返済して 0 円になれば、そのプレイヤーは新リボを使える。
// テーブルにはほかの列(borrow_date / stop_interest など)も残っているが、旧リボの処理では使わない。
public class LegacyServerLoan
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    // 前回返済日。返済(定期・手動)のたびに更新する。
    public DateTime LastPayDate { get; set; }
    // 残債。
    public decimal BorrowAmount { get; set; }
    // 支払額(設定): 定期返済で引き落とす額。0 以下なら定期返済では引き落とさない。
    public decimal PaymentAmount { get; set; }
}
