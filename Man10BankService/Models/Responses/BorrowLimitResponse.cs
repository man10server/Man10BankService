namespace Man10BankService.Models.Responses;

// 借入上限レスポンス。JSONは { "limit": 123 }。
public sealed record BorrowLimitResponse(decimal Limit);
