using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class MoneyLog
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }
    [StringLength(32)]
    public required string PluginName { get; set; }
    public decimal Amount { get; set; }
    [StringLength(128)]
    public required string Note { get; set; }
    [StringLength(128)]
    public required string DisplayNote { get; set; }
    [StringLength(16)]
    public required string Server { get; set; }
    public bool Deposit { get; set; }
    public DateTime Date { get; set; }
}
