namespace Man10BankService.Services;

// VaultService が返す残高ドメイン値(残高 + 版数)。コントローラで VaultBalanceResponse へ変換する。
public sealed record VaultBalanceData(decimal Balance, long Version);

// move の結果。移動後の vault 残高・版数と bank 残高。
public sealed record VaultMoveData(decimal VaultBalance, long VaultVersion, decimal BankBalance);

// transfer(/pay)の結果。送金元・送金先双方の更新後残高・版数。
public sealed record VaultTransferData(VaultBalanceData From, VaultBalanceData To);
