using FluentAssertions;
using Man10BankService.Services;

namespace Test.Services;

// vault session(在席サーバー・sessionId)管理の単体テスト。
// 後勝ち claim・一致時のみ release・接続断での一括失効を検証する。
public class VaultSessionManagerTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    [Fact(DisplayName = "claim: sessionId を発行し Validate が server+sessionId の一致で通る")]
    public void Claim_And_Validate()
    {
        var m = new VaultSessionManager();
        var (claimed, revoked) = m.Claim(Uuid, "player", "dev", "conn-1");

        revoked.Should().BeNull();
        claimed.SessionId.Should().NotBeNullOrEmpty();
        m.Validate(Uuid, "dev", claimed.SessionId).Should().BeTrue();
        m.Validate(Uuid, "other", claimed.SessionId).Should().BeFalse();
        m.Validate(Uuid, "dev", "wrong").Should().BeFalse();
    }

    [Fact(DisplayName = "claim: 後勝ちで旧 session が失効し、旧 sessionId は Validate に落ちる")]
    public void Claim_LastWriteWins()
    {
        var m = new VaultSessionManager();
        var (first, _) = m.Claim(Uuid, "player", "dev", "conn-1");
        var (second, revoked) = m.Claim(Uuid, "player", "dev2", "conn-2");

        revoked.Should().NotBeNull();
        revoked!.SessionId.Should().Be(first.SessionId);
        revoked.Server.Should().Be("dev");

        m.Validate(Uuid, "dev", first.SessionId).Should().BeFalse();
        m.Validate(Uuid, "dev2", second.SessionId).Should().BeTrue();
    }

    [Fact(DisplayName = "release: sessionId 一致時のみ解除し、古い release は新 session を壊さない")]
    public void Release_OnlyWhenMatched()
    {
        var m = new VaultSessionManager();
        var (first, _) = m.Claim(Uuid, "player", "dev", "conn-1");
        var (second, _) = m.Claim(Uuid, "player", "dev2", "conn-2");

        // 旧 sessionId での release は無効(後勝ちの新 session を保持)
        m.Release(Uuid, first.SessionId).Should().BeFalse();
        m.Get(Uuid).Should().NotBeNull();

        m.Release(Uuid, second.SessionId).Should().BeTrue();
        m.Get(Uuid).Should().BeNull();
    }

    [Fact(DisplayName = "ExpireConnection: 対象接続の session だけ一括失効する")]
    public void ExpireConnection_RemovesOnlyThatConnection()
    {
        var m = new VaultSessionManager();
        m.Claim("9c4161a9-0f5f-4317-835c-0bb196a7def1", "p1", "dev", "conn-1");
        m.Claim("9c4161a9-0f5f-4317-835c-0bb196a7def2", "p2", "dev", "conn-1");
        m.Claim("9c4161a9-0f5f-4317-835c-0bb196a7def3", "p3", "dev2", "conn-2");

        var expired = m.ExpireConnection("conn-1");

        expired.Should().HaveCount(2);
        m.Count.Should().Be(1);
        m.Get("9c4161a9-0f5f-4317-835c-0bb196a7def3").Should().NotBeNull();
    }
}
