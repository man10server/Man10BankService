using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// user_vault と user_bank の相互移動方向。
public enum VaultMoveDirection
{
    // /deposit: user_vault -> user_bank(電子マネーを減らす)。
    VaultToBank,

    // /withdraw: user_bank -> user_vault(電子マネーを増やす)。
    BankToVault
}

// user_vault と user_bank の相互移動リクエスト(/deposit /withdraw 用)。ATM には使わない。
// user_vault 行ロック -> user_bank 行ロックの順で 1 DB トランザクションで更新する。
public class VaultMoveRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(UuidValidation.Pattern, ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    [Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額が上限を超えています。")]
    public decimal Amount { get; set; }

    [Required]
    public VaultMoveDirection Direction { get; set; }

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
    }
}
