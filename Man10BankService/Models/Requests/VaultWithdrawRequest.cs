using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー出金リクエスト(原子的差分 -= amount)。残高不足は 409。
// 書き込みは対象の在席サーバーだけが発行できる(単一書き込み者)。sessionId は claim と一致必須。
public class VaultWithdrawRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    [Range(typeof(decimal), VaultAmountLimits.MinText, VaultAmountLimits.MaxSafeText, ErrorMessage = "金額が上限を超えています。")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(16)]
    public required string PluginName { get; set; }

    [Required]
    [StringLength(64)]
    public required string Note { get; set; }

    [Required]
    [StringLength(64)]
    public required string DisplayNote { get; set; }

    [Required]
    [StringLength(16)]
    public required string Server { get; set; }

    // 対象 UUID の現在の session claim 識別子(VaultProvider 8.2)。
    [Required]
    [StringLength(64)]
    public required string SessionId { get; set; }

    // 冪等キー(Provider 送信キュー経由のみ指定)。同一キーの再送には同じ結果を返す。
    [StringLength(36)]
    public string? OperationId { get; set; }

    // 操作元種別(PROVIDER / MAN10_API / ADMIN)。省略時はサービス側で導出する。
    [StringLength(16)]
    public string? Source { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
    }
}
