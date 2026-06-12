using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// 名前解決が null を返したとき PlayerNotFound として 404 に統一されること(DESIGN 1.3)。
// FakePlayerProfileService の既定フォールバックでは検証できないため、明示的に null を返させる。
public class PlayerNotFoundTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string OtherUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private sealed record TestEnv(ControllerHost Host) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static (TestEnv Env, BankController Ctrl) BuildWithUnknownPlayer()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();

        // すべての UUID で名前解決を null にする(プレイヤー不明)
        var profile = new FakePlayerProfileService();
        profile.SetName(Uuid, null);
        profile.SetName(OtherUuid, null);
        var service = new BankService(db.Factory, profile);
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        var host = new ControllerHost { Controller = ctrl, Resources = [db, sp] };
        return (new TestEnv(host), ctrl);
    }

    [Fact(DisplayName = "PlayerNotFound: 入金で名前解決nullは404を返す")]
    public async Task Deposit_UnknownPlayer_ShouldReturn404()
    {
        var (env, ctrl) = BuildWithUnknownPlayer();
        using var _ = env;

        var res = await ctrl.Deposit(new DepositRequest
        {
            Uuid = Uuid, Amount = 100m, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        });

        var notFound = res.Result.Should().BeOfType<NotFoundObjectResult>().Which;
        var pd = notFound.Value.Should().BeOfType<ProblemDetails>().Which;
        pd.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());
    }

    [Fact(DisplayName = "PlayerNotFound: 出金で名前解決nullは404を返す")]
    public async Task Withdraw_UnknownPlayer_ShouldReturn404()
    {
        var (env, ctrl) = BuildWithUnknownPlayer();
        using var _ = env;

        var res = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = Uuid, Amount = 100m, PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        });

        res.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());
    }

    [Fact(DisplayName = "PlayerNotFound: 送金で送金元の名前解決nullは404を返す")]
    public async Task Transfer_UnknownPlayer_ShouldReturn404()
    {
        var (env, ctrl) = BuildWithUnknownPlayer();
        using var _ = env;

        var res = await ctrl.Transfer(new TransferRequest
        {
            FromUuid = Uuid, ToUuid = OtherUuid, Amount = 100m,
            PluginName = "test", Note = "n", DisplayNote = "n", Server = "dev"
        });

        res.Result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());
    }
}
