using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// サーバーローンのレスポンスDTO。
// Principal は残債(BorrowAmount)のうちの元本で、残りが未払いの利息。
// Legacy が true のときは、旧リボ(0.5.0 までの借入。利息なし・借入不可)の残債を表す。
// 旧リボの残債が残っているプレイヤーには、新リボの契約ではなく旧リボを返す。
public sealed record ServerLoanResponse(
    int Id,
    string Player,
    string Uuid,
    DateTime LastPayDate,
    decimal BorrowAmount,
    decimal Principal,
    decimal PaymentAmount,
    bool StopInterest,
    bool Legacy)
{
    public static ServerLoanResponse From(ServerLoan e) =>
        new(e.Id, e.Player, e.Uuid, e.LastPayDate, e.BorrowAmount, e.Principal, e.PaymentAmount,
            e.StopInterest, Legacy: false);

    // 旧リボは利息が付かないので、残債の全額を元本とし、StopInterest は常に true。
    public static ServerLoanResponse FromLegacy(LegacyServerLoan e) =>
        new(e.Id, e.Player, e.Uuid, e.LastPayDate, e.BorrowAmount, e.BorrowAmount, e.PaymentAmount,
            StopInterest: true, Legacy: true);
}
