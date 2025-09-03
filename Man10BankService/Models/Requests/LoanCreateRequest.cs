using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class LoanCreateRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string LendUuid { get; set; }

    [Required]
    [StringLength(16)]
    public required string LendPlayer { get; set; }

    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string BorrowUuid { get; set; }

    [Required]
    [StringLength(16)]
    public required string BorrowPlayer { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public DateTime PaybackDate { get; set; }

    [StringLength(128)]
    public string CollateralItem { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", new[] { nameof(Amount) });
        if (LendUuid == BorrowUuid)
            yield return new ValidationResult("貸手と借手の UUID が同一です。", new[] { nameof(LendUuid), nameof(BorrowUuid) });
    }
}

