using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// 入出金ログのレスポンスDTO。JSONプロパティ名は MoneyLog エンティティと同一(camelCase)。
public sealed record MoneyLogResponse(
    int Id,
    string Player,
    string Uuid,
    string PluginName,
    decimal Amount,
    string Note,
    string DisplayNote,
    string Server,
    bool Deposit,
    DateTime Date)
{
    public static MoneyLogResponse From(MoneyLog e) =>
        new(e.Id, e.Player, e.Uuid, e.PluginName, e.Amount, e.Note, e.DisplayNote, e.Server, e.Deposit, e.Date);
}
