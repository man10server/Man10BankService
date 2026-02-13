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

        var profile = new FakePlayerProfileService();
        var service = new AtmService(db.Factory, profile);
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
        const string uuid = TestConstants.Uuid;

        var req = new AtmLogRequest
        {
            Uuid = uuid,
            Amount = 700,
            Deposit = true,
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        var addResult = await ctrl.AddLog(req);
        addResult.Result.Should().BeOfType<OkObjectResult>();

        var logsResult = await ctrl.GetLogs(uuid, 10);
        var logs = logsResult.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<AtmLog>>().Which;
        
        logs.Should().NotBeEmpty();
        logs[0].Should().BeEquivalentTo(new
        {
            Uuid = uuid,
            Amount = 700m,
            Deposit = true
        }, opt => opt.ExcludingMissingMembers());
    }

    [Fact(DisplayName = "ATMログ追加失敗: 金額不正でValidationProblem・ログ未作成")]
    public async Task AddLog_Invalid_ShouldReturn400_AndNoLog()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;
        const string uuid = TestConstants.Uuid;

        var req = new AtmLogRequest
        {
            Uuid = uuid,
            Amount = 0, // invalid
            Deposit = false,
        };
        ctrl.TryValidateModel(req).Should().BeFalse();

        var post = await ctrl.AddLog(req);
        var postResult = post.Result.Should().BeOfType<ObjectResult>().Which;
        postResult.Value.Should().BeOfType<ValidationProblemDetails>();

        var get = await ctrl.GetLogs(uuid, 10);
        var logs = get.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<AtmLog>>().Which;
        logs.Should().BeEmpty();
    }

    [Fact(DisplayName = "DBダウン時: ATMログ取得は500エラー")]
    public async Task GetLogs_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.GetLogs(TestConstants.Uuid, 10);
        res.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact(DisplayName = "DBダウン時: ATMログ追加は500エラー")]
    public async Task AddLog_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;

        var req = new AtmLogRequest
        {
            Uuid = TestConstants.Uuid,
            Amount = 100,
            Deposit = false,
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.AddLog(req);
        res.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact(DisplayName = "ATMログ取得: 100件投入し limit/offset で中間10件を取得")]
    public async Task GetLogs_Pagination_ShouldReturnMiddleSlice()
    {
        using var host = BuildController();
        var ctrl = (AtmController)host.Controller;
        const string uuid = TestConstants.Uuid;

        for (var i = 1; i <= 100; i++)
        {
            var req = new AtmLogRequest { Uuid = uuid, Amount = i, Deposit = true };
            ctrl.TryValidateModel(req).Should().BeTrue();
            var res = await ctrl.AddLog(req);
            res.Result.Should().BeOfType<OkObjectResult>();
        }

        var get = await ctrl.GetLogs(uuid, limit: 10, offset: 30);
        var logs = get.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<AtmLog>>().Which;
        logs.Should().HaveCount(10);
        var amounts = logs.Select(x => x.Amount).ToArray();
        amounts.Should().BeEquivalentTo(new decimal[] { 70, 69, 68, 67, 66, 65, 64, 63, 62, 61 }, opt => opt.WithStrictOrdering());
    }
}
