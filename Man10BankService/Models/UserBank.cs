namespace Man10BankService.Models;

public class UserBank
{
    public int Id { get; set; }
    public string Player { get; set; } = null!;
    public string Uuid { get; set; } = null!;
    public decimal Balance { get; set; }
}

