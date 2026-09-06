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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// 電子マネー ⇄ 銀行残高の move(1 Tx)を検証する。両残高が同時に整合して動くこと、
// 不足時は 409 で双方不変であること、session claim が必須であることを確認する。
public class VaultMoveTests
{
    private sealed class Host : IDisposable
    {
        public required VaultController Vault { get; init; }
        public required BankController Bank { get; init; }
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
    private const string Server = "dev";

    private static Host Build(decimal? maxBalance = null)
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(VaultController).Assembly);
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(maxBalance == null
                ? []
                : new Dictionary<string, string?> { ["Vault:MaxBalance"] = maxBalance.Value.ToString() })
            .Build();

        var profile = new FakePlayerProfileService();
        var sessions = new VaultSessionManager();
        var channel = new FakeVaultSessionChannel();
        var bank = new BankService(db.Factory, profile);
        var vaultSvc = new VaultService(db.Factory, bank, profile, sessions, channel, config);

        var validator = sp.GetRequiredService<IObjectModelValidator>();
        var metadata = sp.GetRequiredService<IModelMetadataProvider>();

        var vaultCtrl = new VaultController(vaultSvc, new VaultWsHub(sessions))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = validator, MetadataProvider = metadata
        };
        var bankCtrl = new BankController(bank)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = validator, MetadataProvider = metadata
        };

        return new Host { Vault = vaultCtrl, Bank = bankCtrl, Resources = [db, sp] };
    }

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<T>().Which;

    private static async Task<string> ClaimAsync(Host h) =>
        Ok(await h.Vault.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = Uuid, Player = "test-player", Server = Server
        })).SessionId;

    private static VaultMoveRequest Move(decimal amount, VaultMoveDirection dir, string sessionId) => new()
    {
        Uuid = Uuid, Amount = amount, Direction = dir,
        PluginName = "test", Note = "move", DisplayNote = "移動", Server = Server, SessionId = sessionId
    };

    private static async Task SeedVault(Host h, decimal amount, string sessionId) =>
        await h.Vault.Deposit(new VaultDepositRequest
        {
            Uuid = Uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = Server,
            SessionId = sessionId
        });

    private static async Task SeedBank(Host h, decimal amount) =>
        await h.Bank.Deposit(new DepositRequest
        {
            Uuid = Uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = Server
        });

    [Fact(DisplayName = "VaultToBank: 電子マネーが減り銀行が増える(両残高整合)")]
    public async Task Move_VaultToBank()
    {
        using var h = Build();
        var session = await ClaimAsync(h);
        await SeedVault(h, 1000, session);

        var res = Ok(await h.Vault.Move(Move(400, VaultMoveDirection.VaultToBank, session)));
        res.VaultBalance.Should().Be(600m);
        res.BankBalance.Should().Be(400m);

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(600m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(400m);
    }

    [Fact(DisplayName = "BankToVault: 銀行が減り電子マネーが増える(両残高整合)")]
    public async Task Move_BankToVault()
    {
        using var h = Build();
        var session = await ClaimAsync(h);
        await SeedBank(h, 1000);

        var res = Ok(await h.Vault.Move(Move(300, VaultMoveDirection.BankToVault, session)));
        res.VaultBalance.Should().Be(300m);
        res.BankBalance.Should().Be(700m);

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(300m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(700m);
    }

    [Fact(DisplayName = "claim なしの move は 409 で双方不変")]
    public async Task Move_NoClaim_409()
    {
        using var h = Build();
        await SeedBank(h, 1000);

        (await h.Vault.Move(Move(300, VaultMoveDirection.BankToVault, "no-such-session")))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(0m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(1000m);
    }

    [Fact(DisplayName = "VaultToBank: 電子マネー不足は 409 で双方不変")]
    public async Task Move_VaultToBank_Insufficient()
    {
        using var h = Build();
        var session = await ClaimAsync(h);
        await SeedVault(h, 100, session);
        await SeedBank(h, 50);

        (await h.Vault.Move(Move(500, VaultMoveDirection.VaultToBank, session)))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(50m);
    }

    [Fact(DisplayName = "BankToVault: 銀行不足は 409 で双方不変")]
    public async Task Move_BankToVault_Insufficient()
    {
        using var h = Build();
        var session = await ClaimAsync(h);
        await SeedVault(h, 100, session);
        await SeedBank(h, 50);

        (await h.Vault.Move(Move(500, VaultMoveDirection.BankToVault, session)))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(50m);
    }

    [Fact(DisplayName = "BankToVault: 電子マネーが上限を超える move は 409 で双方不変")]
    public async Task Move_BankToVault_ExceedsMax_409()
    {
        using var h = Build(maxBalance: 1000m);
        var session = await ClaimAsync(h);
        await SeedVault(h, 800, session);
        await SeedBank(h, 500);

        (await h.Vault.Move(Move(300, VaultMoveDirection.BankToVault, session)))
            .Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await h.Vault.GetBalance(Uuid)).Balance.Should().Be(800m);
        Ok(await h.Bank.GetBalance(Uuid)).Balance.Should().Be(500m);
    }
}
