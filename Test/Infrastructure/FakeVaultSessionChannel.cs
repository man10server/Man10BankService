using System.Collections.Concurrent;
using Man10BankService.Services;

namespace Test.Infrastructure;

// IVaultSessionChannel のフェイク。接続状態を切り替えられ、失効通知を記録する。
public class FakeVaultSessionChannel : IVaultSessionChannel
{
    public bool Connected { get; set; } = true;
    public string ConnectionId { get; set; } = "conn-1";

    public ConcurrentQueue<(string Server, string Uuid, string SessionId, string NewServer)> Revocations { get; } = new();

    public string? GetConnectionId(string server) => Connected ? ConnectionId : null;

    public Task NotifySessionRevokedAsync(string server, string uuid, string sessionId, string newServer)
    {
        Revocations.Enqueue((server, uuid, sessionId, newServer));
        return Task.CompletedTask;
    }
}
