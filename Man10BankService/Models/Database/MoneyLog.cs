namespace Man10BankService.Models.Database;

public class MoneyLog
{
    public int Id { get; set; }
    public string Player { get; set; }
    public string Uuid { get; set; }
    public string PluginName { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; }
    public string DisplayNote { get; set; }
    public string Server { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
