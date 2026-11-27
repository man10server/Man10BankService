using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

// 他のプレイヤーの契約(server_loan_tbl)に参加しているアカウント。
// 行が無いプレイヤーは自分自身の契約を使う。1 アカウントが参加できる契約は 1 つだけ。
public class ServerLoanMember
{
    // 参加するアカウント
    [StringLength(36)]
    public required string Uuid { get; set; }
    // 参加先の契約の本垢(server_loan_tbl.uuid)
    [StringLength(36)]
    public required string OwnerUuid { get; set; }
    // 紐付けた人(申請した本人、または運営)の名前
    [StringLength(16)]
    public required string LinkedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    // 承認された日時。null の間は承認待ちで、契約は共有されない。
    public DateTime? ApprovedAt { get; set; }
}
