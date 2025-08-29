namespace Man10BankService.Models.Requests;

public class WithdrawRequest
{
    public string Uuid { get; set; } = string.Empty;
    public string Player { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PluginName { get; set; } = "api";
    public string Note { get; set; } = string.Empty;
    public string DisplayNote { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
}

