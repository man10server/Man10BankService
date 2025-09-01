using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class ServerLoanBorrowBodyRequest
{
    [Required]
    [StringLength(16)]
    public required string Player { get; set; }

    [Required]
    public decimal Amount { get; set; }
}

