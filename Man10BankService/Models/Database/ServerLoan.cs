using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// サーバーローンの契約(server_loan_contract)。1 プレイヤーにつき 1 行で、残債を持つ単位になる。
// 他のプレイヤーの契約に参加しているアカウント(ServerLoanMember)は、自分の行ではなく参加先の行を使う。
// 借入枠と延滞の状態は列に持たず、ログ(ServerLoanLog)から求める。
public class ServerLoan
{
    public int Id { get; set; }
    // 契約の持ち主(本垢)の名前と UUID。UUID は一意。
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    // 前回返済日。返済(定期・手動・延滞分の回収)のたびに更新し、残債 0 から借りたときにも置き直す。
    public DateTime LastPayDate { get; set; }
    // 残債(元本 + 利息)。
    public decimal BorrowAmount { get; set; }
    // 残債のうちの元本(借りた額の残り)。残りの「残債 − 元本」が未払いの利息。
    // 返済は利息から先に充てるので、元本が減るのは未払いの利息が無くなってから。
    public decimal Principal { get; set; }
    // 支払額(設定): 定期返済で引き落としてほしい額。最低返済額より小さければ、最低返済額が使われる。
    public decimal PaymentAmount { get; set; }
    // true の間は利息を付けない。
    public bool StopInterest { get; set; }
}
