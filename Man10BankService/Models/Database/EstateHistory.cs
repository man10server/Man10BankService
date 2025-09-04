using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class EstateHistory
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    public DateTime Date { get; set; }

    public decimal Vault { get; set; }
    public decimal Bank { get; set; }
    public decimal Cash { get; set; }
    public decimal EstateAmount { get; set; }
    public decimal Loan { get; set; }
    public decimal Shop { get; set; }
    public decimal Crypto { get; set; }
    public decimal Total { get; set; }
}
