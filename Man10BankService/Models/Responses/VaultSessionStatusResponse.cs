namespace Man10BankService.Models.Responses;

// 対象 UUID の在席サーバー / session 状態(VaultProvider 8.1)。
// online=false のとき server / sessionId は null。
public sealed record VaultSessionStatusResponse(
    bool Online,
    string? Server,
    string? SessionId);
