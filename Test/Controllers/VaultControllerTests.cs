using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Hubs;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Test.Infrastructure;

namespace Test.Controllers;

public class VaultControllerTests
{
    private const string Uuid = TestConstants.Uuid;

    private static ControllerHost BuildController(VaultOptions? options = null)
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(VaultController).Assembly);
        var sp = services.BuildServiceProvider();

        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var notifier = new NullVaultNotifier();
        var service = new VaultService(
            bank, db.Factory, profile, notifier,
            Options.Create(options ?? new VaultOptions()),
            NullLogger<VaultService>.Instance);
        var hub = new VaultWsHub(NullLogger<VaultWsHub>.Instance);

        var ctrl = new VaultController(service, hub)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return new ControllerHost { Controller = ctrl, Resources = [db, sp] };
    }

    private static VaultDepositRequest Deposit(decimal amount, string? op = null, VaultSource? source = null) => new()
    {
        Uuid = Uuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "表示", Server = "dev",
        OperationId = op, Source = source
    };

    private static VaultWithdrawRequest Withdraw(decimal amount, string? op = null) => new()
    {
        Uuid = Uuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "表示", Server = "dev", OperationId = op
    };

    private static T Ok<T>(ActionResult<T> r) =>
        (T)r.Result.Should().BeOfType<OkObjectResult>().Which.Value!;

    [Fact(DisplayName = "入金成功: 残高が増え version が 1 になりログに source/balance_after が記録される")]
    public async Task Deposit_Success()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        var req = Deposit(500);
        ctrl.TryValidateModel(req).Should().BeTrue();
        var res = Ok(await ctrl.Deposit(req));
        res.Balance.Should().Be(500m);
        res.Version.Should().Be(1L);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = 500m, Deposit = true, Source = "MAN10_API", BalanceAfter = 500m
        }, o => o.ExcludingMissingMembers());
    }

    [Fact(DisplayName = "入金検証: 0 円は無効、負数は無効、小数は無効")]
    public void Deposit_InvalidAmounts()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;
        ctrl.TryValidateModel(Deposit(0)).Should().BeFalse();
        ctrl.TryValidateModel(Deposit(-5)).Should().BeFalse();
        ctrl.TryValidateModel(Deposit(1.5m)).Should().BeFalse();
    }

    [Fact(DisplayName = "出金成功: 残高が減り version が増える")]
    public async Task Withdraw_Success()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Deposit(1000));
        var res = Ok(await ctrl.Withdraw(Withdraw(600)));
        res.Balance.Should().Be(400m);
        res.Version.Should().Be(2L);
    }

    [Fact(DisplayName = "出金失敗: 残高不足は 409 で残高不変")]
    public async Task Withdraw_Insufficient_409()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Deposit(100));
        var ng = await ctrl.Withdraw(Withdraw(500));
        ng.Result.Should().BeOfType<ConflictObjectResult>();
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(100m);
    }

    [Fact(DisplayName = "冪等再送: 同一 operationId の入金は一度しか適用されない")]
    public async Task Deposit_Idempotent()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        var first = Ok(await ctrl.Deposit(Deposit(500, op: "op-1")));
        first.Balance.Should().Be(500m);

        // 同一 operationId の再送 -> 再適用せず現在残高を返す
        var second = Ok(await ctrl.Deposit(Deposit(500, op: "op-1")));
        second.Balance.Should().Be(500m);

        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(500m);
        var logs = Ok(await ctrl.GetLogs(Uuid, 100));
        logs.Count(l => l.OperationId == "op-1").Should().Be(1);
    }

    [Fact(DisplayName = "上限超過: 既定上限を超える入金は 409(BalanceLimitExceeded)")]
    public async Task Deposit_OverLimit_409()
    {
        using var host = BuildController(new VaultOptions { MaxBalance = 1000m });
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Deposit(800));
        var ng = await ctrl.Deposit(Deposit(300));
        ng.Result.Should().BeOfType<ConflictObjectResult>();
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(800m);
    }

    [Fact(DisplayName = "Provider 入金: source=PROVIDER がログに記録される")]
    public async Task Deposit_ProviderSource()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Deposit(100, op: "p-1", source: VaultSource.PROVIDER));
        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Source.Should().Be("PROVIDER");
    }

    [Fact(DisplayName = "設定取得: 既定値(1兆/3000/3000)を返す")]
    public void GetConfig_Default()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;
        var cfg = Ok(ctrl.GetConfig());
        cfg.MaxBalance.Should().Be(1_000_000_000_000m);
        cfg.JoinReadyDelayMillis.Should().Be(3000);
        cfg.QuitDrainTimeoutMillis.Should().Be(3000);
    }

    [Fact(DisplayName = "設定取得: MaxBalance が不正なら 500(fail-closed)")]
    public void GetConfig_InvalidMax_500()
    {
        using var host = BuildController(new VaultOptions { MaxBalance = 0m });
        var ctrl = (VaultController)host.Controller;
        (ctrl.GetConfig().Result as ObjectResult)!.StatusCode.Should().Be(500);
    }

    [Fact(DisplayName = "set: 絶対値設定で残高が上書きされ version が増える")]
    public async Task Set_Success()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Deposit(300));
        var set = Ok(await ctrl.Set(new VaultSetRequest
        {
            Uuid = Uuid, Amount = 50, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = "dev"
        }));
        set.Balance.Should().Be(50m);
        set.Version.Should().Be(2L);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Source.Should().Be("ADMIN");
        logs[0].Amount.Should().Be(-250m); // 300 -> 50 の差分
    }
}
