using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class ChequeUseRequest
{
    [Required]
    [StringLength(16)]
    public required string Player { get; set; }
}
