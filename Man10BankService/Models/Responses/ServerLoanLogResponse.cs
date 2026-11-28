using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバーローンログのレスポンスDTO。JSONプロパティ名は ServerLoanLog エンティティと同一(camelCase)。
// 旧リボのログには操作者と操作後の残債の記録が無いので、ActorUuid・DebtAfter・PrincipalAfter は null になる。
public sealed record ServerLoanLogResponse(
    int Id,
    string Player,
    string Uuid,
    string Action,
    decimal Amount,
    DateTime Date,
    string? ActorUuid,
    decimal? DebtAfter,
    decimal? PrincipalAfter)
{
    public static ServerLoanLogResponse From(ServerLoanLog e) =>
        new(e.Id, e.Player, e.Uuid, e.Action, e.Amount, e.Date, e.ActorUuid, e.DebtAfter, e.PrincipalAfter);

    public static ServerLoanLogResponse FromLegacy(LegacyServerLoanLog e) =>
        new(e.Id, e.Player, e.Uuid, e.Action, e.Amount, e.Date, null, null, null);
}
