namespace Man10BankService.Models;

public class EstateHistory
{
    public int Id { get; set; }
    public string? Uuid { get; set; }
    public DateTime? Date { get; set; }
    public string? Player { get; set; }

    public decimal Vault { get; set; }
    public decimal Bank { get; set; }
    public decimal Cash { get; set; }
    public decimal EstateAmount { get; set; }
    public decimal Loan { get; set; }
    public decimal Shop { get; set; }
    public decimal Crypto { get; set; }
    public decimal Total { get; set; }
}

