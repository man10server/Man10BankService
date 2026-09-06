namespace Man10BankService.Models.Responses;

// 電子マネー残高レスポンス。JSONは { "balance": 123, "version": 42 }。
// version は user_vault.Version で、Paper 側がキャッシュ適用順序の判定に使う(VaultProvider 4.3)。
public sealed record VaultBalanceResponse(decimal Balance, long Version);
