namespace Man10BankService.Models;

public class ServerLoan
{
    public int Id { get; set; }
    public string Player { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime LastPayDate { get; set; }
    public decimal BorrowAmount { get; set; }
    public decimal PaymentAmount { get; set; }
    public int FailedPayment { get; set; }
    public bool StopInterest { get; set; }
}
