namespace Man10BankService.Models;

public class ServerEstateHistory
{
    public int Id { get; set; }
    public decimal Vault { get; set; }
    public decimal Bank { get; set; }
    public decimal Cash { get; set; }
    public decimal EstateAmount { get; set; }
    public decimal Loan { get; set; }
    public decimal Crypto { get; set; }
    public decimal Shop { get; set; }
    public decimal Total { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public int Hour { get; set; }
    public DateTime Date { get; set; }
}
