using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// 金銭操作の名前解決は「ログイン時登録の名簿(player_profile) → 口座(user_bank)の名前」の順で行い、
// 外部 API(Mojang)を使わないこと。改名は名簿の再登録で user_bank / money_log の名前にも反映されること。
public class PlayerNameResolutionTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string OtherUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private sealed record TestEnv(ControllerHost Host, IDbContextFactory<BankDbContext> DbFactory, PlayerRegistryService Registry) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static (TestEnv Env, BankController Ctrl) Build()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();

        var service = new BankService(db.Factory, new RegistryPlayerProfileService(db.Factory));
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        var host = new ControllerHost { Controller = ctrl, Resources = [db, sp] };
        return (new TestEnv(host, db.Factory, new PlayerRegistryService(db.Factory)), ctrl);
    }

    private static DepositRequest Deposit(string uuid, decimal amount) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
    };

    private static TransferRequest Transfer(decimal amount) => new()
    {
        FromUuid = Uuid, ToUuid = OtherUuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
    };

    private static decimal BalanceOf(ActionResult<BalanceResponse> res) =>
        res.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;

    private static void NotFound(ActionResult<BalanceResponse> res) =>
        res.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());

    private static async Task RegisterAsync(TestEnv env, string uuid, string name) =>
        (await env.Registry.RegisterAsync(new PlayerRegisterRequest { Uuid = uuid, Name = name })).IsSuccess.Should().BeTrue();

    [Fact(DisplayName = "名前解決: 名簿に登録された名前で口座とログが作られる")]
    public async Task Deposit_RegisteredPlayer_ShouldUseRegisteredName()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        await RegisterAsync(env, Uuid, "Steve");

        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 500m))).Should().Be(500m);

        await using var db = await env.DbFactory.CreateDbContextAsync();
        (await db.UserBanks.AsNoTracking().SingleAsync(x => x.Uuid == Uuid)).Player.Should().Be("Steve");
        (await db.MoneyLogs.AsNoTracking().SingleAsync(x => x.Uuid == Uuid)).Player.Should().Be("Steve");
    }

    [Fact(DisplayName = "名前解決: 改名後に再登録すると、次の取引で口座の名前が更新されログにも新しい名前が残る")]
    public async Task Rename_ShouldPropagateToUserBankAndLogs()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        await RegisterAsync(env, Uuid, "OldName");
        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 500m))).Should().Be(500m);

        // 改名してログイン → 名簿が更新される
        await RegisterAsync(env, Uuid, "NewName");
        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 100m))).Should().Be(600m);

        await using var db = await env.DbFactory.CreateDbContextAsync();
        (await db.UserBanks.AsNoTracking().SingleAsync(x => x.Uuid == Uuid)).Player.Should().Be("NewName");
        var logs = await db.MoneyLogs.AsNoTracking().Where(x => x.Uuid == Uuid).OrderBy(x => x.Id).ToListAsync();
        logs.Select(l => l.Player).Should().Equal("OldName", "NewName");
    }

    [Fact(DisplayName = "名前解決: 名簿に無くても口座があればその名前で入出金・送金できる(移行期の既存口座)")]
    public async Task ExistingAccountWithoutRegistry_ShouldStillWork()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        await using (var db = await env.DbFactory.CreateDbContextAsync())
        {
            db.UserBanks.Add(new UserBank { Uuid = Uuid, Player = "Legacy", Balance = 1000m });
            db.UserBanks.Add(new UserBank { Uuid = OtherUuid, Player = "LegacyTo", Balance = 0m });
            await db.SaveChangesAsync();
        }

        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 100m))).Should().Be(1100m);
        BalanceOf(await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = Uuid, Amount = 100m, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        })).Should().Be(1000m);
        BalanceOf(await ctrl.Transfer(Transfer(300m))).Should().Be(700m);
        BalanceOf(await ctrl.GetBalance(OtherUuid)).Should().Be(300m);
    }

    [Fact(DisplayName = "名前解決: 名簿にも口座にも無い送金先は404になり、送金元の残高は変わらない(外部APIへは問い合わせない)")]
    public async Task Transfer_ToUnknownPlayer_ShouldReturn404_AndKeepFromBalance()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        await RegisterAsync(env, Uuid, "Steve");
        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 1000m))).Should().Be(1000m);

        NotFound(await ctrl.Transfer(Transfer(300m)));
        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(1000m);

        // 送金先がログイン(名簿登録)すれば送金できる
        await RegisterAsync(env, OtherUuid, "Alex");
        BalanceOf(await ctrl.Transfer(Transfer(300m))).Should().Be(700m);
        await using var db = await env.DbFactory.CreateDbContextAsync();
        (await db.UserBanks.AsNoTracking().SingleAsync(x => x.Uuid == OtherUuid)).Player.Should().Be("Alex");
    }

    [Fact(DisplayName = "名前解決: 名簿にも口座にも無いUUIDへの入金は404")]
    public async Task Deposit_UnknownPlayer_ShouldReturn404()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        NotFound(await ctrl.Deposit(Deposit(Uuid, 100m)));
    }
}
