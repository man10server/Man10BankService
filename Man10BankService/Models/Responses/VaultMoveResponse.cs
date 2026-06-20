namespace Man10BankService.Models.Responses;

// user_vault と user_bank の移動結果。移動後の双方の残高を返す。
public sealed record VaultMoveResponse(
    decimal VaultBalance,
    long VaultVersion,
    decimal BankBalance);
