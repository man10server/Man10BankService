using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class ServerLoan
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    public DateTime BorrowDate { get; set; }
    public DateTime LastPayDate { get; set; }
    public decimal BorrowAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public int FailedPayment { get; set; }
    public bool StopInterest { get; set; }
}
