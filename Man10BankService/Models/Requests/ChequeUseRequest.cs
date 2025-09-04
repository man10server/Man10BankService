using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class ChequeUseRequest
{
    [Required]
    [StringLength(36)]
    [RegularExpression(@"^[0-9a-fA-F-]{36}$", ErrorMessage = "UUID の形式が不正です。")]
    public required string Uuid { get; set; }
}
