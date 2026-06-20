namespace Man10BankService.Models.Responses;

// 電子マネー送金(/pay)結果。送金元・送金先双方の更新後残高・版数を返す。
public sealed record VaultTransferResponse(
    decimal FromBalance,
    long FromVersion,
    decimal ToBalance,
    long ToVersion);
