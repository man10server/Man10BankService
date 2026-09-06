using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 管理用: 電子マネー残高を絶対値で設定する(差分を vault_log へ記録)。
// 他の書き込みと同様、対象が在席するサーバー(claim 一致)からのみ実行できる(VaultProvider 5.4)。
// 0 への設定も許容するため Amount は 0 以上を許可する。
public class VaultSetRequest : IValidatableObject
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount < 0m)
            yield return new ValidationResult("金額は 0 以上である必要があります。", [nameof(Amount)]);
    }
}
