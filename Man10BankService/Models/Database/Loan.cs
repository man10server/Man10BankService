namespace Man10BankService.Models;

public class Loan
{
    public int Id { get; set; }
    public string LendPlayer { get; set; } = string.Empty;
    public string LendUuid { get; set; } = string.Empty;
    public string BorrowPlayer { get; set; } = string.Empty;
    public string BorrowUuid { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime PaybackDate { get; set; }
    public decimal Amount { get; set; }
    public string CollateralItem { get; set; } = string.Empty;
}
