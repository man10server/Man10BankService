using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class LoanCreateRequest : IValidatableObject
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string LendUuid { get; set; }

    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string BorrowUuid { get; set; }

    [Required]
    public decimal BorrowAmount { get; set; }
    
    [Required]
    public decimal RepayAmount { get; set; }

    [Required]
    public DateTime PaybackDate { get; set; }

    public string CollateralItem { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BorrowAmount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(BorrowAmount)]);
        if (RepayAmount <= 0m)
            yield return new ValidationResult("金額は 0 より大きい必要があります。", [nameof(RepayAmount)]);
        if (LendUuid == BorrowUuid)
            yield return new ValidationResult("貸手と借手の UUID が同一です。", [nameof(LendUuid), nameof(BorrowUuid)]);
    }
}
