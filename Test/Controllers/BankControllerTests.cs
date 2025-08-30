using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;
using System.Linq;

namespace Test.Controllers;

public class BankControllerTests
{
    private static ControllerHost BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();

        var service = new BankService(db.Factory);
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return new ControllerHost
        {
            Controller = ctrl,
            Resources = [db, sp]
        };
    }

    [Fact]
    public async Task Deposit_Success_ShouldIncreaseBalance_AndWriteLog()
    {
        using var host = BuildController();
        var ctrl = host.Controller;
        var req = new DepositRequest
        {
            Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Player = "steve",
            Amount = 500,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();
        var result = await ctrl.Deposit(req) as ObjectResult;
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ApiResult<decimal>;
        body!.Data.Should().Be(500);

        var balRes = await ctrl.GetBalance(req.Uuid) as ObjectResult;
        (balRes!.Value as ApiResult<decimal>)!.Data.Should().Be(500);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10) as ObjectResult;
        var logs = (logsRes!.Value as ApiResult<List<MoneyLog>>)!.Data!;
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = 500m,
            Deposit = true,
            Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Player = "steve",
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        });
    }

    [Fact]
    public async Task GetBalance_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        // DB をダウンさせる（:memory: は接続クローズでスキーマが揮発）
        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.GetBalance("deadbeef-dead-beef-dead-beefdeadbeef") as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = res.Value as ApiResult<decimal>;
        body!.Message.Should().StartWith("残高取得に失敗しました");
    }

    [Fact]
    public async Task Deposit_Invalid_ShouldNotChangeBalance_AndReturn400()
    {
        using var host = BuildController();
        var ctrl = host.Controller;
        var req = new DepositRequest
        {
            Uuid = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            Player = "alex",
            Amount = 0,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeFalse();
        var result = await ctrl.Deposit(req) as ObjectResult;
        result.Should().NotBeNull();
        result!.Value.Should().BeOfType<ValidationProblemDetails>();

        var balRes = await ctrl.GetBalance(req.Uuid) as ObjectResult;
        (balRes!.Value as ApiResult<decimal>)!.Data.Should().Be(0);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10, 0) as ObjectResult;
        (logsRes!.Value as ApiResult<List<MoneyLog>>)!.Data!.Count.Should().Be(0);
    }

    [Fact]
    public async Task Withdraw_Success_Then_Insufficient_Should409_And_NoChange()
    {
        using var host = BuildController();
        var ctrl = host.Controller;
        const string uuid = "cccccccc-cccc-cccc-cccc-cccccccccccc";

        await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 1000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        });

        var ok = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 600,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        }) as ObjectResult;
        ok!.StatusCode.Should().Be(200);
        var bal1 = await ctrl.GetBalance(uuid) as ObjectResult;
        (bal1!.Value as ApiResult<decimal>)!.Data.Should().Be(400);

        var ng = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 500,
            PluginName = "test",
            Note = "w2",
            DisplayNote = "出金2",
            Server = "dev"
        }) as ObjectResult;
        ng!.StatusCode.Should().Be(409);

        var bal2 = await ctrl.GetBalance(uuid) as ObjectResult;
        (bal2!.Value as ApiResult<decimal>)!.Data.Should().Be(400);
    }
}
