namespace Man10BankService.Models;

public class MoneyLog
{
    public int Id { get; set; }
    public string Player { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public string DisplayNote { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
