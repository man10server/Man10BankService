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

    [Fact(DisplayName = "DBダウン時: 残高取得は500エラー")]
    public async Task GetBalance_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.GetBalance("deadbeef-dead-beef-dead-beefdeadbeef") as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = res.Value as ApiResult<decimal>;
        body!.Message.Should().StartWith("残高取得に失敗しました");
    }
    
    [Fact(DisplayName = "入金成功: 残高が増加しログが記録される")]
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
    
    [Fact(DisplayName = "入金失敗: 金額不正で400・残高とログは変化なし")]
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

    [Fact(DisplayName = "入金失敗: 入金は500エラー")]
    public async Task Deposit_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var req = new DepositRequest
        {
            Uuid = "dddddddd-dddd-dddd-dddd-dddddddddddd",
            Player = "alex",
            Amount = 100,
            PluginName = "test",
            Note = "down",
            DisplayNote = "DBダウン",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        // DB をダウン
        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.Deposit(req) as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = res.Value as ApiResult<decimal>;
        body!.Message.Should().StartWith("入金に失敗しました");
    }
    
    [Fact(DisplayName = "出金成功: 残高が減少しログが記録される")]
    public async Task Withdraw_Success_ShouldDecreaseBalance_AndWriteLog()
    {
        using var host = BuildController();
        var ctrl = host.Controller;
        const string uuid = "ffffffff-ffff-ffff-ffff-ffffffffffff";

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

        var balRes = await ctrl.GetBalance(uuid) as ObjectResult;
        (balRes!.Value as ApiResult<decimal>)!.Data.Should().Be(400);

        var logsRes = await ctrl.GetLogs(uuid, 10) as ObjectResult;
        var logs = (logsRes!.Value as ApiResult<List<MoneyLog>>)!.Data!;
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = -600m,
            Deposit = false,
            Uuid = uuid,
            Player = "alex",
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        });
    }

    [Fact(DisplayName = "出金成功後の残高不足: 2回目は409で残高不変")]
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


    [Fact(DisplayName = "DBダウン時: 出金は500エラー")]
    public async Task Withdraw_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var req = new WithdrawRequest
        {
            Uuid = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
            Player = "alex",
            Amount = 100,
            PluginName = "test",
            Note = "down",
            DisplayNote = "DBダウン",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        // DB をダウン
        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.Withdraw(req) as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = res.Value as ApiResult<decimal>;
        body!.Message.Should().StartWith("出金に失敗しました");
    }
}
