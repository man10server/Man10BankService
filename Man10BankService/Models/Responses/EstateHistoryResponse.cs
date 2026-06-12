using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// 資産履歴のレスポンスDTO。JSONプロパティ名は EstateHistory エンティティと同一(camelCase)。
public sealed record EstateHistoryResponse(
    int Id,
    string Player,
    string Uuid,
    DateTime Date,
    decimal Vault,
    decimal Bank,
    decimal Cash,
    decimal EstateAmount,
    decimal Loan,
    decimal Shop,
    decimal Crypto,
    decimal Total)
{
    public static EstateHistoryResponse From(EstateHistory e) =>
        new(e.Id, e.Player, e.Uuid, e.Date, e.Vault, e.Bank, e.Cash, e.EstateAmount, e.Loan, e.Shop, e.Crypto, e.Total);
}
