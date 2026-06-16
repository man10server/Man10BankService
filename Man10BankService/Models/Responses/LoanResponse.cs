using Man10BankService.Models.Database;

namespace Man10BankService.Models.Responses;

// 個人間貸付のレスポンスDTO。JSONプロパティ名は Loan エンティティと同一(camelCase)。
// 内部監査列(collateralReleasedAt/collateralReleaseReason)は公開しない。
public sealed record LoanResponse(
    int Id,
    string LendPlayer,
    string LendUuid,
    string BorrowPlayer,
    string BorrowUuid,
    DateTime BorrowDate,
    DateTime PaybackDate,
    decimal Amount,
    string? CollateralItem,
    bool CollateralReleased)
{
    public static LoanResponse From(Loan e) =>
        new(e.Id, e.LendPlayer, e.LendUuid, e.BorrowPlayer, e.BorrowUuid, e.BorrowDate, e.PaybackDate,
            e.Amount, e.CollateralItem, e.CollateralReleased);
}
