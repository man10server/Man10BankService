using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバーローンのレスポンスDTO。JSONプロパティ名は ServerLoan エンティティと同一(camelCase)。
public sealed record ServerLoanResponse(
    int Id,
    string Player,
    string Uuid,
    DateTime BorrowDate,
    DateTime LastPayDate,
    decimal BorrowAmount,
    decimal PaymentAmount,
    int FailedPayment,
    bool StopInterest)
{
    public static ServerLoanResponse From(ServerLoan e) =>
        new(e.Id, e.Player, e.Uuid, e.BorrowDate, e.LastPayDate, e.BorrowAmount, e.PaymentAmount,
            e.FailedPayment, e.StopInterest);
}
