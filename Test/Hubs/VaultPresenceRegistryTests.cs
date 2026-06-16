using FluentAssertions;
using Man10BankService.Hubs;

namespace Test.Hubs;

// presence(UUID→接続)の登録/解除/失効/後勝ちを検証する。
public class VaultPresenceRegistryTests
{
    private const string UuidA = "0a1b2c3d-0000-0000-0000-000000000001";
    private const string UuidB = "0a1b2c3d-0000-0000-0000-000000000002";

    [Fact(DisplayName = "join した UUID は対象接続で Find できる")]
    public void Join_ThenFind()
    {
        var reg = new VaultPresenceRegistry();
        var conn = new object();
        reg.Join(UuidA, conn);
        reg.Find(UuidA).Should().BeSameAs(conn);
        reg.Find(UuidB).Should().BeNull();
    }

    [Fact(DisplayName = "quit すると以後 Find できない")]
    public void Quit_Removes()
    {
        var reg = new VaultPresenceRegistry();
        var conn = new object();
        reg.Join(UuidA, conn);
        reg.Quit(UuidA, conn).Should().BeTrue();
        reg.Find(UuidA).Should().BeNull();
    }

    [Fact(DisplayName = "別接続が後勝ちで上書き(サーバー移動)。旧接続の quit は無視される")]
    public void Join_LastWriteWins()
    {
        var reg = new VaultPresenceRegistry();
        var oldConn = new object();
        var newConn = new object();

        reg.Join(UuidA, oldConn);
        reg.Join(UuidA, newConn); // 後勝ち

        reg.Find(UuidA).Should().BeSameAs(newConn);

        // 旧接続が遅れて quit しても、現在の登録(newConn)は消えない
        reg.Quit(UuidA, oldConn).Should().BeFalse();
        reg.Find(UuidA).Should().BeSameAs(newConn);
    }

    [Fact(DisplayName = "接続断: その接続の presence を一括失効する")]
    public void ExpireConnection_BulkRemoves()
    {
        var reg = new VaultPresenceRegistry();
        var conn1 = new object();
        var conn2 = new object();

        reg.Join(UuidA, conn1);
        reg.Join(UuidB, conn1);
        reg.Join("0a1b2c3d-0000-0000-0000-000000000003", conn2);

        var expired = reg.ExpireConnection(conn1);
        expired.Should().Be(2);
        reg.Find(UuidA).Should().BeNull();
        reg.Find(UuidB).Should().BeNull();
        reg.Count.Should().Be(1); // conn2 のぶんは残る
    }
}
