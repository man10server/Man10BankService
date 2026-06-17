using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Services;
using Test.Infrastructure;

namespace Test.Services;

// uuid→名前のローカルDB解決(Mojang 廃止後の実装)の単体テスト(SQLite)。
public class LocalDbPlayerProfileServiceTests
{
    private static string NewUuid() => Guid.NewGuid().ToString();

    [Fact(DisplayName = "UUIDが空や不正形式ならnullを返す(PlayerNotFound相当)")]
    public async Task GetNameByUuid_ShouldReturnNull_WhenInvalid()
    {
        using var db = TestDbFactory.Create();
        var service = new LocalDbPlayerProfileService(db.Factory);

        (await service.GetNameByUuidAsync("")).Should().BeNull();
        (await service.GetNameByUuidAsync("not-a-uuid")).Should().BeNull();
        (await service.GetNameByUuidAsync("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")).Should().BeNull();
    }

    [Fact(DisplayName = "user_bank に名前があればそれを返す")]
    public async Task GetNameByUuid_ShouldReturnName_WhenKnownInUserBank()
    {
        using var db = TestDbFactory.Create();
        var uuid = NewUuid();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.UserBanks.Add(new UserBank { Player = "Alice", Uuid = uuid, Balance = 0m });
            await ctx.SaveChangesAsync();
        }

        var service = new LocalDbPlayerProfileService(db.Factory);

        (await service.GetNameByUuidAsync(uuid)).Should().Be("Alice");
    }

    [Fact(DisplayName = "user_vault のみに名前があってもそれを返す")]
    public async Task GetNameByUuid_ShouldReturnName_WhenKnownInUserVaultOnly()
    {
        using var db = TestDbFactory.Create();
        var uuid = NewUuid();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.UserVaults.Add(new UserVault { Player = "Bob", Uuid = uuid, Balance = 0m, Version = 0L });
            await ctx.SaveChangesAsync();
        }

        var service = new LocalDbPlayerProfileService(db.Factory);

        (await service.GetNameByUuidAsync(uuid)).Should().Be("Bob");
    }

    [Fact(DisplayName = "形式は正しいがローカル未記録なら空文字を返す(口座作成を妨げない)")]
    public async Task GetNameByUuid_ShouldReturnEmpty_WhenWellFormedButUnknown()
    {
        using var db = TestDbFactory.Create();
        var service = new LocalDbPlayerProfileService(db.Factory);

        (await service.GetNameByUuidAsync(NewUuid())).Should().Be(string.Empty);
    }
}
