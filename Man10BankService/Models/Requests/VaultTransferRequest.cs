using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー送金リクエスト(/pay 用)。fromUuid から toUuid へ amount を移動する。
// 送金元・送金先が同一 Paper 上でオンラインであることの検証は呼び出し元 Paper の VaultService が行う。
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
    [Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額が上限を超えています。")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(32)]
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

    [StringLength(64)]
    public string? OperationId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("金額は整数で指定してください。", [nameof(Amount)]);
        if (string.Equals(FromUuid, ToUuid, StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("送金元と送金先に同じUUIDは指定できません。", [nameof(ToUuid)]);
    }
}
