using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

public class EstateControllerTests
{
    private sealed record TestEnv(ControllerHost Host, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static TestEnv BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(EstateController).Assembly);

        var sp = services.BuildServiceProvider();
        var estateService = new EstateService(db.Factory);

        var ctrl = new EstateController(estateService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };

        var host = new ControllerHost
        {
            Controller = ctrl,
            Resources = [db, sp]
        };
        return new TestEnv(host, db.Factory);
    }

    [Fact(DisplayName = "estate: 全値でsnapshot→latestに反映される")]
    public async Task UpdateSnapshot_AllValues_ShouldReflectInLatest()
    {
        using var env = BuildController();
        var ctrl = (EstateController)env.Host.Controller;
        const string uuid = "not32hex"; // 外部解決を避けるため意図的に不正なUUID

        var req = new EstateUpdateRequest
        {
            Cash = 100m,
            Vault = 200m,
            EstateAmount = 300m,
            Shop = 50m
        };

        var post = await ctrl.UpdateSnapshot(uuid, req) as ObjectResult;
        post!.StatusCode.Should().Be(200);
        var updated = (post.Value as ApiResult<bool>)!;
        updated.Data.Should().BeTrue();

        var get = await ctrl.GetLatest(uuid) as ObjectResult;
        get!.StatusCode.Should().Be(200);
        var latest = (get.Value as ApiResult<Estate?>)!.Data!;

        latest.Should().BeEquivalentTo(new
        {
            Cash = 100m,
            Vault = 200m,
            EstateAmount = 300m,
            Shop = 50m,
            Bank = 0m,
            Loan = 0m,
            Crypto = 0m,
            Total = 100m + 200m + 300m + 50m
        },options => options.IncludingAllDeclaredProperties());
        
        var histRes = await ctrl.GetHistory(uuid) as ObjectResult;
        histRes!.StatusCode.Should().Be(200);
        var history = (histRes.Value as ApiResult<List<EstateHistory>>)!.Data!;
        history.Count.Should().Be(1);
    }

    [Fact(DisplayName = "estate: 同一値でsnapshot→更新false・履歴は増えない")]
    public async Task UpdateSnapshot_SameValues_ShouldReturnFalse_And_NotAppendHistory()
    {
        using var env = BuildController();
        var ctrl = (EstateController)env.Host.Controller;
        const string uuid = "not32hex2";

        var req = new EstateUpdateRequest
        {
            Cash = 10m,
            Vault = 20m,
            EstateAmount = 30m,
            Shop = 40m
        };

        var firstUpdate = await ctrl.UpdateSnapshot(uuid, req) as ObjectResult;
        firstUpdate!.StatusCode.Should().Be(200);
        var firstUpdateResult = (firstUpdate.Value as ApiResult<bool>)!;
        firstUpdateResult.Data.Should().BeTrue();

        var firstHistory = await ctrl.GetHistory(uuid) as ObjectResult;
        firstHistory!.StatusCode.Should().Be(200);
        var history1 = (firstHistory.Value as ApiResult<List<EstateHistory>>)!.Data!;
        history1.Count.Should().Be(1);

        var secondUpdate = await ctrl.UpdateSnapshot(uuid, req) as ObjectResult;
        secondUpdate!.StatusCode.Should().Be(200);
        var secondUpdateResult = (secondUpdate.Value as ApiResult<bool>)!;
        secondUpdateResult.Data.Should().BeFalse();

        var secondHistory = await ctrl.GetHistory(uuid) as ObjectResult;
        secondHistory!.StatusCode.Should().Be(200);
        var history2 = (secondHistory.Value as ApiResult<List<EstateHistory>>)!.Data!;
        history2.Count.Should().Be(1);

        var latestResult = await ctrl.GetLatest(uuid) as ObjectResult;
        latestResult!.StatusCode.Should().Be(200);
        var latest = (latestResult.Value as ApiResult<Estate?>)!.Data!;
        latest.Should().BeEquivalentTo(new
        {
            Cash = 10m,
            Vault = 20m,
            EstateAmount = 30m,
            Shop = 40m,
            Bank = 0m,
            Loan = 0m,
            Crypto = 0m,
            Total = 10m + 20m + 30m + 40m
        }, options => options.IncludingAllDeclaredProperties());
    }
}
