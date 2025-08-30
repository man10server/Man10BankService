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
using System.Linq;
using Test.Infrastructure;

namespace Test.Controllers;

public class AtmControllerTests
{
    private static ControllerHost BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(AtmController).Assembly);
        var sp = services.BuildServiceProvider();

        var service = new AtmService(db.Factory);
        var ctrl = new AtmController(service)
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

    [Fact(DisplayName = "ATMログ追加成功: ログが保存され取得できる")]
    public async Task AddLog_Success_ShouldCreateLog_AndRetrievable()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;
        const string uuid = "11111111-1111-1111-1111-111111111111";

        var req = new AtmLogRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 700,
            Deposit = true,
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        var post = await ctrl.AddLog(req) as ObjectResult;
        post!.StatusCode.Should().Be(200);
        var created = (post.Value as ApiResult<AtmLog>)!.Data!;
        created.Uuid.Should().Be(uuid);
        created.Player.Should().Be("alex");
        created.Amount.Should().Be(700);
        created.Deposit.Should().BeTrue();

        var get = await ctrl.GetLogs(uuid, 10) as ObjectResult;
        get!.StatusCode.Should().Be(200);
        var logs = (get.Value as ApiResult<List<AtmLog>>)!.Data!;
        logs.Should().NotBeEmpty();
        logs[0].Should().BeEquivalentTo(new
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 700m,
            Deposit = true
        }, opt => opt.ExcludingMissingMembers());
    }

    [Fact(DisplayName = "ATMログ追加失敗: 金額不正で400・ログ未作成")]
    public async Task AddLog_Invalid_ShouldReturn400_AndNoLog()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;
        const string uuid = "22222222-2222-2222-2222-222222222222";

        var req = new AtmLogRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 0, // invalid
            Deposit = false,
        };
        ctrl.TryValidateModel(req).Should().BeFalse();

        var post = await ctrl.AddLog(req) as ObjectResult;
        post.Should().NotBeNull();
        post!.Value.Should().BeOfType<ValidationProblemDetails>();

        var get = await ctrl.GetLogs(uuid, 10) as ObjectResult;
        get!.StatusCode.Should().Be(200);
        var logs = (get.Value as ApiResult<List<AtmLog>>)!.Data!;
        logs.Should().BeEmpty();
    }

    [Fact(DisplayName = "DBダウン時: ATMログ取得は500エラー")]
    public async Task GetLogs_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.GetLogs("33333333-3333-3333-3333-333333333333", 10) as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = (res.Value as ApiResult<List<AtmLog>>)!
;        body.Message.Should().StartWith("ATMログの取得に失敗しました");
    }

    [Fact(DisplayName = "DBダウン時: ATMログ追加は500エラー")]
    public async Task AddLog_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;

        var req = new AtmLogRequest
        {
            Uuid = "44444444-4444-4444-4444-444444444444",
            Player = "alex",
            Amount = 100,
            Deposit = false,
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.AddLog(req) as ObjectResult;
        res!.StatusCode.Should().Be(500);
        var body = (res.Value as ApiResult<AtmLog>)!;
        body.Message.Should().StartWith("ATMログの追加に失敗しました");
    }
}
