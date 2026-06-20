namespace Man10BankService.Models.Responses;

// Vault 設定レスポンス。残高上限と Provider 移動緩和設定を Paper へ配布する。
public sealed record VaultConfigResponse(
    decimal MaxBalance,
    long JoinReadyDelayMillis,
    long QuitDrainTimeoutMillis);
