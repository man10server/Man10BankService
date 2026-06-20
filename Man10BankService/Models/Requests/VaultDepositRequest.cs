using System.ComponentModel.DataAnnotations;
using Man10BankService.Models.Database;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

// 電子マネー入金リクエスト。内製 API と Provider 送信待ちキューの両方で使う。
// OperationId が指定された場合は冪等キーとして UNIQUE 扱いになる。
public class VaultDepositRequest : IValidatableObject
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

    // 冪等キー。指定時は UNIQUE。同一 OperationId の再送には同じ結果を返す。
    [StringLength(64)]
    public string? OperationId { get; set; }

    // 操作元種別。未指定時はサービス側で MAN10_API を既定とする。
    public VaultSource? Source { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
        if (Amount != decimal.Truncate(Amount))
            yield return new ValidationResult("金額は整数で指定してください。", [nameof(Amount)]);
    }
}
