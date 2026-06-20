using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Hubs;
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

public class VaultMoveTransferTests
{
    private const string From = TestConstants.LendUuid;
    private const string To = TestConstants.BorrowUuid;

    private static ControllerHost BuildController(VaultOptions? options = null)
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(VaultController).Assembly);
        var sp = services.BuildServiceProvider();

        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var service = new VaultService(
            bank, db.Factory, profile, new NullVaultNotifier(),
            Options.Create(options ?? new VaultOptions()),
            NullLogger<VaultService>.Instance);
        var hub = new VaultWsHub(NullLogger<VaultWsHub>.Instance);

        var ctrl = new VaultController(service, hub)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return new ControllerHost { Controller = ctrl, Resources = [db, sp] };
    }

    private static T Ok<T>(ActionResult<T> r) =>
        (T)r.Result.Should().BeOfType<OkObjectResult>().Which.Value!;

    private static VaultDepositRequest Dep(string uuid, decimal amount) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "n", DisplayNote = "表示", Server = "dev"
    };

    private static VaultMoveRequest Move(string uuid, decimal amount, VaultMoveDirection dir, string? op = null) => new()
    {
        Uuid = uuid, Amount = amount, Direction = dir,
        PluginName = "test", Note = "n", DisplayNote = "表示", Server = "dev", OperationId = op
    };

    [Fact(DisplayName = "move VaultToBank: 電子マネーが減り銀行が増える(1 Tx)")]
    public async Task Move_VaultToBank()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 1000));
        var res = Ok(await ctrl.Move(Move(From, 600, VaultMoveDirection.VaultToBank)));
        res.VaultBalance.Should().Be(400m);
        res.BankBalance.Should().Be(600m);
    }

    [Fact(DisplayName = "move BankToVault: 銀行が減り電子マネーが増える")]
    public async Task Move_BankToVault()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 1000));
        await ctrl.Move(Move(From, 600, VaultMoveDirection.VaultToBank)); // vault=400, bank=600
        var res = Ok(await ctrl.Move(Move(From, 500, VaultMoveDirection.BankToVault)));
        res.VaultBalance.Should().Be(900m);
        res.BankBalance.Should().Be(100m);
    }

    [Fact(DisplayName = "move VaultToBank: 電子マネー不足は 409")]
    public async Task Move_VaultToBank_Insufficient()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 100));
        var ng = await ctrl.Move(Move(From, 500, VaultMoveDirection.VaultToBank));
        ng.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "move BankToVault: 銀行不足は 409")]
    public async Task Move_BankToVault_Insufficient()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        // bank には残高なし
        var ng = await ctrl.Move(Move(From, 500, VaultMoveDirection.BankToVault));
        ng.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "move 冪等: 同一 operationId は一度しか適用されない")]
    public async Task Move_Idempotent()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 1000));
        var first = Ok(await ctrl.Move(Move(From, 600, VaultMoveDirection.VaultToBank, op: "m-1")));
        first.VaultBalance.Should().Be(400m);
        var second = Ok(await ctrl.Move(Move(From, 600, VaultMoveDirection.VaultToBank, op: "m-1")));
        second.VaultBalance.Should().Be(400m); // 再適用されない
        second.BankBalance.Should().Be(600m);
    }

    [Fact(DisplayName = "transfer: 送金元が減り送金先が増える")]
    public async Task Transfer_Success()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 1000));
        var res = Ok(await ctrl.Transfer(new VaultTransferRequest
        {
            FromUuid = From, ToUuid = To, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = "dev"
        }));
        res.FromBalance.Should().Be(700m);
        res.ToBalance.Should().Be(300m);
    }

    [Fact(DisplayName = "transfer: 送金元残高不足は 409")]
    public async Task Transfer_Insufficient()
    {
        using var host = BuildController();
        var ctrl = (VaultController)host.Controller;

        await ctrl.Deposit(Dep(From, 100));
        var ng = await ctrl.Transfer(new VaultTransferRequest
        {
            FromUuid = From, ToUuid = To, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = "dev"
        });
        ng.Result.Should().BeOfType<ConflictObjectResult>();
    }
}
