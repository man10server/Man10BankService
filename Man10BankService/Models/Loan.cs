namespace Man10BankService.Models;

public class Loan
{
    public int Id { get; set; }
    public string? LendPlayer { get; set; }
    public string? LendUuid { get; set; }
    public string? BorrowPlayer { get; set; }
    public string? BorrowUuid { get; set; }
    public DateTime? BorrowDate { get; set; }
    public DateTime? PaybackDate { get; set; }
    public decimal Amount { get; set; }
    public string? CollateralItem { get; set; }
}

