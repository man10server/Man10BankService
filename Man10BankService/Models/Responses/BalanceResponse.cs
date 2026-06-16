namespace Man10BankService.Models.Responses;

// 残高レスポンス。JSONは { "balance": 123 }。
public sealed record BalanceResponse(decimal Balance);
