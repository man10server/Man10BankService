namespace Man10BankService.Models.Database;

public class ServerLoanLog
{
    public int Id { get; set; }
    public string Player { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // borrow/repay/interest
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
