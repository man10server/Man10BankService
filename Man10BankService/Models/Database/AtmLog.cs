using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class AtmLog
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    public decimal Amount { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
