namespace Man10BankService.Models.Responses;

// session claim の結果。以後の書き込みに必須の sessionId と、確定残高・version、
// 残高上限の権威設定値(Vault:MaxBalance)を配布する(VaultProvider 8.1)。
public sealed record VaultSessionClaimResponse(
    string SessionId,
    decimal Balance,
    long Version,
    decimal MaxBalance);
