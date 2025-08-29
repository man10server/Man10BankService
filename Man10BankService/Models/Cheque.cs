namespace Man10BankService.Models;

public class Cheque
{
    public int Id { get; set; }
    public string? Player { get; set; }
    public string? Uuid { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? UseDate { get; set; }
    public string? UsePlayer { get; set; }
    public bool? Used { get; set; }
}

