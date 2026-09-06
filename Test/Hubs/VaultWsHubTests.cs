using System.Text.Json;
using FluentAssertions;
using Man10BankService.Hubs;
using Man10BankService.Services;

namespace Test.Hubs;

// session 失効イベント JSON の形(VaultProvider 7.4)と、未接続サーバーへの通知が no-op であることを検証する。
public class VaultWsHubTests
{
    private const string Uuid = "0a1b2c3d-0000-0000-0000-000000000001";

    [Fact(DisplayName = "session 失効イベント JSON が type/uuid/sessionId/newServer を持つ")]
    public void BuildSessionRevokedJson_Shape()
    {
        var json = VaultWsHub.BuildSessionRevokedJson(Uuid, "session-1", "lobby2");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("session_revoked");
        root.GetProperty("uuid").GetString().Should().Be(Uuid);
        root.GetProperty("sessionId").GetString().Should().Be("session-1");
        root.GetProperty("newServer").GetString().Should().Be("lobby2");
    }

    [Fact(DisplayName = "未接続サーバーへの失効通知は no-op(例外を投げない)")]
    public async Task NotifyRevoked_NotConnected_NoOp()
    {
        var hub = new VaultWsHub(new VaultSessionManager());
        hub.ConnectionCount.Should().Be(0);
        hub.GetConnectionId("lobby").Should().BeNull();

        await hub.NotifySessionRevokedAsync("lobby", Uuid, "session-1", "lobby2"); // 例外なく完了する
    }
}
