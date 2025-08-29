namespace Man10BankService.Models;

public class AtmLog
{
    public int Id { get; set; }
    public string Player { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
