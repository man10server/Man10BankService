using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー ⇄ 銀行残高の移動方向。
public enum VaultMoveDirection
{
    // 電子マネー → 銀行(/deposit 相当)
    VaultToBank,
    // 銀行 → 電子マネー(/withdraw 相当)
    BankToVault
}

// 電子マネー ⇄ 銀行残高を単一トランザクションで移動する(ATM/`/deposit`/`/withdraw` 用、VaultProvider 7.2)。
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
    }
}
