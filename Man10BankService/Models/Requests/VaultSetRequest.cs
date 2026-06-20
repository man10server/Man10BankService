using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 管理者用の電子マネー残高絶対値設定(setBalance / editvault)。
// 在席状況を問わず受理する。source はサービス側で ADMIN 固定。
public class VaultSetRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    // 設定する絶対値。0 以上を許可する。
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
        if (Amount < 0m)
            yield return new ValidationResult("金額は 0 以上で指定してください。", [nameof(Amount)]);
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("金額は整数で指定してください。", [nameof(Amount)]);
    }
}
