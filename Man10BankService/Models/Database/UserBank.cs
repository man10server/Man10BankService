using System.ComponentModel.DataAnnotations;

namespace Man10BankService.Models.Database;

public class UserBank
{
    public int Id { get; set; }
    [StringLength(16)]
    public required string Player { get; set; }
    [StringLength(36)]
    public required string Uuid { get; set; }

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
