using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// 電子マネー取引ログのレスポンスDTO。JSONプロパティ名は VaultLog エンティティと同一(camelCase)。
public sealed record VaultLogResponse(
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
    public static VaultLogResponse From(VaultLog e) =>
        new(e.Id, e.Player, e.Uuid, e.PluginName, e.Amount, e.Note, e.DisplayNote, e.Server, e.Deposit, e.Date);
}
