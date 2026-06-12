using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// 小切手のレスポンスDTO。JSONプロパティ名は Cheque エンティティと同一(camelCase)。
public sealed record ChequeResponse(
    int Id,
    string Player,
    string Uuid,
    decimal Amount,
    string Note,
    DateTime Date,
    DateTime UseDate,
    string UsePlayer,
    bool Used,
    bool Op)
{
    public static ChequeResponse From(Cheque e) =>
        new(e.Id, e.Player, e.Uuid, e.Amount, e.Note, e.Date, e.UseDate, e.UsePlayer, e.Used, e.Op);
}
