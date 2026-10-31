using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// 名前解決は user_bank の口座を優先し、口座が無い初回のみ Mojang(IPlayerProfileService)へ問い合わせること。
// Mojang が落ちていても既存口座への入出金・送金が失敗しない(=結果不明のタイムアウトを誘発しない)ことを検証する。
public class PlayerNameResolutionTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string OtherUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private sealed record TestEnv(ControllerHost Host, FakePlayerProfileService Profile) : IDisposable
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

        var profile = new FakePlayerProfileService();
        var service = new BankService(db.Factory, profile);
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        var host = new ControllerHost { Controller = ctrl, Resources = [db, sp] };
        return (new TestEnv(host, profile), ctrl);
    }

    private static DepositRequest Deposit(string uuid, decimal amount) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
    };

    private static decimal BalanceOf(ActionResult<BalanceResponse> res) =>
        res.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;

    [Fact(DisplayName = "名前解決: 口座が既にあれば Mojang が null を返しても入出金・送金は成功する")]
    public async Task ExistingAccount_ShouldNotDependOnProfileService()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        // 初回入金で口座(名前付き)を作成しておく
        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 1000m))).Should().Be(1000m);
        BalanceOf(await ctrl.Deposit(Deposit(OtherUuid, 10m))).Should().Be(10m);

        // 以降 Mojang は応答不能(null)になる
        env.Profile.SetName(Uuid, null);
        env.Profile.SetName(OtherUuid, null);

        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 100m))).Should().Be(1100m);
        BalanceOf(await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = Uuid, Amount = 100m, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        })).Should().Be(1000m);
        BalanceOf(await ctrl.Transfer(new TransferRequest
        {
            FromUuid = Uuid, ToUuid = OtherUuid, Amount = 300m,
            PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        })).Should().Be(700m);
        BalanceOf(await ctrl.GetBalance(OtherUuid)).Should().Be(310m);
    }

    [Fact(DisplayName = "名前解決: 口座が無い送金先は Mojang で見つからなければ404になり送金元の残高は変わらない")]
    public async Task Transfer_ToUnknownNewPlayer_ShouldReturn404_AndKeepFromBalance()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(Uuid, 1000m))).Should().Be(1000m);
        env.Profile.SetName(OtherUuid, null);

        var res = await ctrl.Transfer(new TransferRequest
        {
            FromUuid = Uuid, ToUuid = OtherUuid, Amount = 300m,
            PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        });
        res.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());

        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(1000m);
    }
}
