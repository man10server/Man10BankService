using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Man10BankService.Models.Database;

public class Loan
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string LendPlayer { get; set; }
    [StringLength(36)]
    public required string LendUuid { get; set; }
    [StringLength(16)]
    public required string BorrowPlayer { get; set; }
    [StringLength(36)]
    public required string BorrowUuid { get; set; }
    public DateTime BorrowDate { get; set; }
    public DateTime PaybackDate { get; set; }
    public decimal Amount { get; set; }
    [StringLength(128)]
    public string? CollateralItem { get; set; }
    public bool CollateralReleased { get; set; }
    [JsonIgnore]
    public DateTime? CollateralReleasedAt { get; set; }
    [StringLength(32)]
    [JsonIgnore]
    public string? CollateralReleaseReason { get; set; }
}
