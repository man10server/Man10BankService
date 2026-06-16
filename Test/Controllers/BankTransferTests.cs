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

// 送金 API(POST /api/Bank/transfer)の検証。
public class BankTransferTests
{
    private const string FromUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string ToUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private sealed record TestEnv(ControllerHost Host, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static TestEnv BuildController()
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
        return new TestEnv(host, db.Factory);
    }

    private static TransferRequest NewTransfer(decimal amount) => new()
    {
        FromUuid = FromUuid,
        ToUuid = ToUuid,
        Amount = amount,
        PluginName = "test",
        Note = "transfer",
        DisplayNote = "送金テスト",
        Server = "dev"
    };

    private static async Task SeedAsync(BankController ctrl, string uuid, decimal amount)
    {
        var res = await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Amount = amount,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        });
        res.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(DisplayName = "transfer: 正常系は送金元の新残高を返し、両者残高とログ2件が整合する")]
    public async Task Transfer_Success_ShouldMoveBalance_AndWriteTwoLogs()
    {
        using var env = BuildController();
        var ctrl = (BankController)env.Host.Controller;
        await SeedAsync(ctrl, FromUuid, 1000m);

        var res = await ctrl.Transfer(NewTransfer(300m));
        var newFrom = res.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;
        newFrom.Should().Be(700m);

        var fromBal = (await ctrl.GetBalance(FromUuid)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;
        fromBal.Should().Be(700m);

        var toBal = (await ctrl.GetBalance(ToUuid)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;
        toBal.Should().Be(300m);

        // 送金元: 出金ログ(-300), 送金先: 入金ログ(+300)
        var fromLogs = await GetLogsAsync(env.DbFactory, FromUuid);
        fromLogs.First().Should().BeEquivalentTo(new { Amount = -300m, Deposit = false, Note = "transfer" });
        var toLogs = await GetLogsAsync(env.DbFactory, ToUuid);
        toLogs.First().Should().BeEquivalentTo(new { Amount = 300m, Deposit = true, Note = "transfer" });
    }

    [Fact(DisplayName = "transfer: 残高不足は409でロールバックされ、両者残高もログも変化しない")]
    public async Task Transfer_Insufficient_Should409_And_RollbackBoth()
    {
        using var env = BuildController();
        var ctrl = (BankController)env.Host.Controller;
        await SeedAsync(ctrl, FromUuid, 100m);

        var res = await ctrl.Transfer(NewTransfer(500m));
        var conflict = res.Result.Should().BeOfType<ConflictObjectResult>().Which;
        var pd = conflict.Value.Should().BeOfType<ProblemDetails>().Which;
        pd.Extensions["code"].Should().Be(ErrorCode.InsufficientFunds.ToString());

        var fromBal = (await ctrl.GetBalance(FromUuid)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;
        fromBal.Should().Be(100m);

        // 送金先は口座未作成のまま(0円)。原子性により入金ログも残らない。
        var toLogs = await GetLogsAsync(env.DbFactory, ToUuid);
        toLogs.Should().BeEmpty();

        var fromLogs = await GetLogsAsync(env.DbFactory, FromUuid);
        fromLogs.Count(l => l.Note == "transfer").Should().Be(0);
    }

    [Fact(DisplayName = "transfer: 同一UUIDはモデル検証で弾かれる")]
    public void Transfer_SameUuid_ShouldFailModelValidation()
    {
        using var env = BuildController();
        var ctrl = (BankController)env.Host.Controller;
        var req = new TransferRequest
        {
            FromUuid = FromUuid,
            ToUuid = FromUuid,
            Amount = 100m,
            PluginName = "test",
            Note = "self",
            DisplayNote = "自己送金",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeFalse();
    }

    [Fact(DisplayName = "transfer: 同一UUIDがサービスへ届いても400で拒否される")]
    public async Task Transfer_SameUuid_AtService_ShouldReturn400()
    {
        using var env = BuildController();
        var ctrl = (BankController)env.Host.Controller;
        await SeedAsync(ctrl, FromUuid, 1000m);

        var req = new TransferRequest
        {
            FromUuid = FromUuid,
            ToUuid = FromUuid,
            Amount = 100m,
            PluginName = "test",
            Note = "self",
            DisplayNote = "自己送金",
            Server = "dev"
        };
        var res = await ctrl.Transfer(req);
        res.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static async Task<List<MoneyLog>> GetLogsAsync(IDbContextFactory<BankDbContext> f, string uuid)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.MoneyLogs.AsNoTracking()
            .Where(x => x.Uuid == uuid)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .ToListAsync();
    }
}
