namespace Man10BankService.Models;

public class Cheque
{
    public int Id { get; set; }
    public string Player { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime UseDate { get; set; }
    public string UsePlayer { get; set; } = string.Empty;
    public bool Used { get; set; }
}
