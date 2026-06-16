using System.Collections.Concurrent;
using Man10BankService.Services;

namespace Test.Infrastructure;

// VaultService の push 呼び出しを記録するフェイク。実 WebSocket を張らずに
// 「コミット後に正しい確定残高+version で push されたか」を検証する。
public sealed class FakeVaultNotifier : IVaultNotifier
{
    public ConcurrentQueue<VaultBalanceChange> Pushes { get; } = new();

    public Task PushBalanceAsync(VaultBalanceChange change)
    {
        Pushes.Enqueue(change);
        return Task.CompletedTask;
    }
}
