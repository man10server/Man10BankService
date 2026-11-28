using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバーローンのレスポンスDTO。JSONプロパティ名は ServerLoan エンティティと同一(camelCase)。
public sealed record ServerLoanResponse(
    int Id,
    string Player,
    string Uuid,
    DateTime LastPayDate,
    decimal BorrowAmount,
    decimal PaymentAmount,
    bool StopInterest)
{
    public static ServerLoanResponse From(ServerLoan e) =>
        new(e.Id, e.Player, e.Uuid, e.LastPayDate, e.BorrowAmount, e.PaymentAmount,
            e.StopInterest);
}
