namespace Man10BankService.Services;

// 残高確定後に Paper の VaultService へ通知する push の内容。
public sealed record VaultBalanceChange(
    string Uuid,
    decimal Balance,
    long Version,
    string Cause,
    string? OperationId,
    string OriginServer);

// 残高確定(コミット後)に Provider キャッシュ収束用の push を行う通知器。
// 実体は WebSocket ハブ。WebSocket が無い構成では no-op 実装でも良い(定期再同期で補う)。
public interface IVaultNotifier
{
    Task PushBalanceAsync(VaultBalanceChange change, CancellationToken ct = default);
}

// WebSocket を使わない構成・テスト向けの no-op 実装。
public sealed class NullVaultNotifier : IVaultNotifier
{
    public Task PushBalanceAsync(VaultBalanceChange change, CancellationToken ct = default) => Task.CompletedTask;
}
