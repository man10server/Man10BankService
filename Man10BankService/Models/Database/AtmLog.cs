namespace Man10BankService.Models.Database;

public class AtmLog
{
    public int Id { get; set; }
    public string Player { get; set; }
    public string Uuid { get; set; }
    public decimal Amount { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
