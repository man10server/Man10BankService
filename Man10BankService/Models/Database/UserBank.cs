namespace Man10BankService.Models;

public class UserBank
{
    public int Id { get; set; }
    public string Player { get; set; } = null!;
    public string Uuid { get; set; } = null!;

    private decimal _balance;
    public decimal Balance
    {
        get => _balance;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(Balance), "所持金がマイナスになることはできません");
            _balance = value;
        }
    }
}
