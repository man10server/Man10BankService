namespace Man10BankService.Models.Responses;

// 電子マネー送金(/pay)の結果。送金元・送金先の両残高+version を返す(VaultProvider 8.1)。
// 残高 push を持たないため、Paper 側は本応答で両者のキャッシュを収束させる。
public sealed record VaultTransferResponse(
    decimal FromBalance,
    long FromVersion,
    decimal ToBalance,
    long ToVersion);
