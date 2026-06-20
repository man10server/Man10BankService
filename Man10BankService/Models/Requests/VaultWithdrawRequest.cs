using System.ComponentModel.DataAnnotations;
using Man10BankService.Models.Database;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー出金リクエスト。内製 API と Provider 送信待ちキューの両方で使う。
// 残高不足はサービス側で 409(InsufficientFunds)。
public class VaultWithdrawRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

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

    public VaultSource? Source { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("金額は整数で指定してください。", [nameof(Amount)]);
    }
}
