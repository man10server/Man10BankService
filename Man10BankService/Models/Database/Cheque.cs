using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class Cheque
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    public decimal Amount { get; set; }
    [StringLength(128)]
    public string Note { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime UseDate { get; set; }
    [StringLength(16)]
    public string UsePlayer { get; set; } = string.Empty;
    public bool Used { get; set; }
}
