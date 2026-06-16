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
using Test.Infrastructure;

namespace Test.Controllers;

// 電子マネー ⇄ 銀行残高の move(1 Tx)を検証する。両残高が同時に整合して動くこと、
// 不足時は 409 で双方不変であることを確認する。
public class VaultMoveTests
{
    private sealed class Host : IDisposable
    {
        public required VaultController Vault { get; init; }
        public required BankController Bank { get; init; }
        public required FakeVaultNotifier Notifier { get; init; }
        public required List<IDisposable> Resources { get; init; }

        public void Dispose()
        {
            foreach (var d in Resources)
            {
                try { d.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    private const string Uuid = TestConstants.Uuid;

    private static Host Build()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(VaultController).Assembly);
        var sp = services.BuildServiceProvider();

        var profile = new FakePlayerProfileService();
        var notifier = new FakeVaultNotifier();
        var bank = new BankService(db.Factory, profile);
        var vaultSvc = new VaultService(db.Factory, bank, profile, notifier);

        var validator = sp.GetRequiredService<IObjectModelValidator>();
        var metadata = sp.GetRequiredService<IModelMetadataProvider>();

        var vaultCtrl = new VaultController(vaultSvc, new VaultWsHub())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = validator, MetadataProvider = metadata
        };
        var bankCtrl = new BankController(bank)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = validator, MetadataProvider = metadata
        };

        return new Host { Vault = vaultCtrl, Bank = bankCtrl, Notifier = notifier, Resources = [db, sp] };
    }

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<T>().Which;

    private static VaultMoveRequest Move(decimal amount, VaultMoveDirection dir) => new()
    {
        Uuid = Uuid, Amount = amount, Direction = dir,
        PluginName = "test", Note = "move", DisplayNote = "移動", Server = "dev"
    };

    private async Task SeedVault(Host h, decimal amount) =>
        await h.Vault.Deposit(new VaultDepositRequest
        {
            Uuid = Uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        });

    private async Task SeedBank(Host h, decimal amount) =>
        await h.Bank.Deposit(new DepositRequest
        {
            Uuid = Uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        });

    [Fact(DisplayName = "VaultToBank: 電子マネーが減り銀行が増える(両残高整合)")]
    public async Task Move_VaultToBank()
    {
        using var h = Build();
        await SeedVault(h, 1000);
        h.Notifier.Pushes.Clear();

        var res = Ok(await h.Vault.Move(Move(400, VaultMoveDirection.VaultToBank)));
        res.VaultBalance.Should().Be(600m);
        res.BankBalance.Should().Be(400m);

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(600m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(400m);

        // 電子マネー側の確定残高が BANK_MOVE で push される
        h.Notifier.Pushes.Should().ContainSingle();
        h.Notifier.Pushes.TryPeek(out var c).Should().BeTrue();
        c!.Cause.Should().Be("BANK_MOVE");
        c.Balance.Should().Be(600m);
    }

    [Fact(DisplayName = "BankToVault: 銀行が減り電子マネーが増える(両残高整合)")]
    public async Task Move_BankToVault()
    {
        using var h = Build();
        await SeedBank(h, 1000);

        var res = Ok(await h.Vault.Move(Move(300, VaultMoveDirection.BankToVault)));
        res.VaultBalance.Should().Be(300m);
        res.BankBalance.Should().Be(700m);

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(300m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(700m);
    }

    [Fact(DisplayName = "VaultToBank: 電子マネー不足は 409 で双方不変")]
    public async Task Move_VaultToBank_Insufficient()
    {
        using var h = Build();
        await SeedVault(h, 100);
        await SeedBank(h, 50);
        h.Notifier.Pushes.Clear();

        (await h.Vault.Move(Move(500, VaultMoveDirection.VaultToBank)))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(50m);
        h.Notifier.Pushes.Should().BeEmpty();
    }

    [Fact(DisplayName = "BankToVault: 銀行不足は 409 で双方不変")]
    public async Task Move_BankToVault_Insufficient()
    {
        using var h = Build();
        await SeedVault(h, 100);
        await SeedBank(h, 50);

        (await h.Vault.Move(Move(500, VaultMoveDirection.BankToVault)))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(50m);
    }
}
