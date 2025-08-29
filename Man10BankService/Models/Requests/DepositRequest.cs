using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Requests;

public class DepositRequest
{
    [Required]
    public required string Uuid { get; set; }

    [Required]
    public required string Player { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public required string PluginName { get; set; }

    [Required]
    public required string Note { get; set; }

    [Required]
    public required string DisplayNote { get; set; }

    [Required]
    public required string Server { get; set; }
}
