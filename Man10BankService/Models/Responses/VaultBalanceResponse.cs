namespace Man10BankService.Models.Responses;

// 電子マネー残高レスポンス。JSON は { "balance": 123, "version": 4 }。
public sealed record VaultBalanceResponse(decimal Balance, long Version);
