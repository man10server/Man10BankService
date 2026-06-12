using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバー資産履歴のレスポンスDTO。JSONプロパティ名は ServerEstateHistory エンティティと同一(camelCase)。
public sealed record ServerEstateHistoryResponse(
    int Id,
    decimal Vault,
    decimal Bank,
    decimal Cash,
    decimal EstateAmount,
    decimal Loan,
    decimal Crypto,
    decimal Shop,
    decimal Total,
    int Year,
    int Month,
    int Day,
    int Hour,
    DateTime Date)
{
    public static ServerEstateHistoryResponse From(ServerEstateHistory e) =>
        new(e.Id, e.Vault, e.Bank, e.Cash, e.EstateAmount, e.Loan, e.Crypto, e.Shop, e.Total,
            e.Year, e.Month, e.Day, e.Hour, e.Date);
}
