using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー送金(/pay)リクエスト。fromUuid から toUuid へ amount を単一トランザクションで移動する。
// 送金元・送金先の両方が要求元サーバーに在席(claim 一致)している場合だけ許可する(VaultProvider 8.2)。
public class VaultTransferRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string FromUuid { get; set; }

    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string ToUuid { get; set; }

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

    // 送金元 UUID の session claim 識別子。
    [Required]
    [StringLength(64)]
    public required string FromSessionId { get; set; }

    // 送金先 UUID の session claim 識別子。
    [Required]
    [StringLength(64)]
    public required string ToSessionId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);

        if (string.Equals(FromUuid, ToUuid, StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("送金元と送金先に同じUUIDは指定できません。", [nameof(ToUuid)]);
    }
}
