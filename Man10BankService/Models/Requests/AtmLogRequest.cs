using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class AtmLogRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public bool Deposit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(Amount)]);
    }
}
