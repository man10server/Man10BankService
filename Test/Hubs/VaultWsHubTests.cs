using System.Text.Json;
using FluentAssertions;
using Man10BankService.Hubs;
using Man10BankService.Services;

namespace Test.Hubs;

// 残高イベント JSON の形(VaultProvider 5.3)と、在席不明 UUID への push が no-op であることを検証する。
public class VaultWsHubTests
{
    private const string Uuid = "0a1b2c3d-0000-0000-0000-000000000001";

    [Fact(DisplayName = "残高イベント JSON が type/uuid/balance/version/cause/originServer/ts を持つ")]
    public void BuildBalanceEventJson_Shape()
    {
        var change = new VaultBalanceChange(Uuid, 123450m, 42L, "DEPOSIT", "lobby");
        var ts = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);

        var json = VaultWsHub.BuildBalanceEventJson(change, ts);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("balance");
        root.GetProperty("uuid").GetString().Should().Be(Uuid);
        root.GetProperty("balance").GetDecimal().Should().Be(123450m);
        root.GetProperty("version").GetInt64().Should().Be(42L);
        root.GetProperty("cause").GetString().Should().Be("DEPOSIT");
        root.GetProperty("originServer").GetString().Should().Be("lobby");
        root.GetProperty("ts").GetString().Should().Be("2026-06-16T10:00:00Z");
    }

    [Fact(DisplayName = "在席不明 UUID への push は no-op(例外を投げない)")]
    public async Task PushBalance_UnknownUuid_NoOp()
    {
        var hub = new VaultWsHub();
        hub.ConnectionCount.Should().Be(0);

        var change = new VaultBalanceChange(Uuid, 100m, 1L, "DEPOSIT", "lobby");
        await hub.PushBalanceAsync(change); // 例外なく完了する

        hub.PresenceCount.Should().Be(0);
    }
}
