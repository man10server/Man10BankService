using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class ServerLoanBorrowBodyRequest
{
    [Required]
    public decimal Amount { get; set; }
}

