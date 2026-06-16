namespace Man10BankService.Models.Responses;

// 電子マネー ⇄ 銀行移動の結果。両残高を返す(VaultProvider 7.2)。
// vaultVersion は移動後の user_vault.Version。
public sealed record VaultMoveResponse(decimal VaultBalance, decimal BankBalance, long VaultVersion);
