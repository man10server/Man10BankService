using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Test.Infrastructure;

namespace Test.Services;

// 名前解決は「ログイン時登録の名簿(player_profile) → 口座(user_bank)の名前」の順で、外部 API を使わないこと。
public class RegistryPlayerProfileServiceTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    [Fact(DisplayName = "名簿に登録があればその名前を返す(口座の古い名前より優先)")]
    public async Task RegisteredName_ShouldWin_OverUserBank()
    {
        using var db = TestDbFactory.Create();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.UserBanks.Add(new UserBank { Uuid = Uuid, Player = "OldName", Balance = 0m });
            await ctx.SaveChangesAsync();
        }
        await new PlayerRegistryService(db.Factory).RegisterAsync(new PlayerRegisterRequest { Uuid = Uuid, Name = "NewName" });

        var service = new RegistryPlayerProfileService(db.Factory);
        (await service.GetNameByUuidAsync(Uuid)).Should().Be("NewName");
    }

    [Fact(DisplayName = "名簿に無ければ口座の名前で補う")]
    public async Task UnregisteredWithAccount_ShouldFallbackToUserBank()
    {
        using var db = TestDbFactory.Create();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.UserBanks.Add(new UserBank { Uuid = Uuid, Player = "AccountName", Balance = 100m });
            await ctx.SaveChangesAsync();
        }

        var service = new RegistryPlayerProfileService(db.Factory);
        (await service.GetNameByUuidAsync(Uuid)).Should().Be("AccountName");
    }

    [Fact(DisplayName = "名簿にも口座にも無いUUID、空・不正なUUIDは null")]
    public async Task Unknown_ShouldReturnNull()
    {
        using var db = TestDbFactory.Create();
        var service = new RegistryPlayerProfileService(db.Factory);
        (await service.GetNameByUuidAsync(Uuid)).Should().BeNull();
        (await service.GetNameByUuidAsync("")).Should().BeNull();
        (await service.GetNameByUuidAsync("not-a-uuid")).Should().BeNull();
    }
}
