using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバーローンログのレスポンスDTO。JSONプロパティ名は ServerLoanLog エンティティと同一(camelCase)。
public sealed record ServerLoanLogResponse(
    int Id,
    string Player,
    string Uuid,
    string Action,
    decimal Amount,
    DateTime Date)
{
    public static ServerLoanLogResponse From(ServerLoanLog e) =>
        new(e.Id, e.Player, e.Uuid, e.Action, e.Amount, e.Date);
}
