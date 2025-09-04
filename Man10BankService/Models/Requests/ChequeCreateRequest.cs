using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class ChequeCreateRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [StringLength(64)]
    public string Note { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
    }
}
