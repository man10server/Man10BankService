using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// ATMログのレスポンスDTO。JSONプロパティ名は AtmLog エンティティと同一(camelCase)。
public sealed record AtmLogResponse(
    int Id,
    string Player,
    string Uuid,
    decimal Amount,
    bool Deposit,
    DateTime Date)
{
    public static AtmLogResponse From(AtmLog e) =>
        new(e.Id, e.Player, e.Uuid, e.Amount, e.Deposit, e.Date);
}
