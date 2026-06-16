using System.ComponentModel.DataAnnotations;
using Man10BankService.Validation;

namespace Man10BankService.Models.Requests;

public class ServerLoanBorrowBodyRequest
{
    [Required]
    [Range(typeof(decimal), AmountLimits.MinText, AmountLimits.MaxText, ErrorMessage = "金額が上限を超えています。")]
    public decimal Amount { get; set; }
}

