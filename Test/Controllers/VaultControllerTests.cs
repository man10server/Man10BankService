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
using System.Collections.Concurrent;

namespace Test.Controllers;

// 電子マネー(Vault)コントローラの単体テスト(SQLite)。
// deposit/withdraw/transfer/set/ensure/logs の挙動と、コミット後 push が発行されることを確認する。
public class VaultControllerTests
{
    private sealed class Host : IDisposable
    {
        public required VaultController Controller { get; init; }
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

    private static Host BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(VaultController).Assembly);
        var sp = services.BuildServiceProvider();

        var profile = new FakePlayerProfileService();
        var notifier = new FakeVaultNotifier();
        var bank = new BankService(db.Factory, profile);
        var service = new VaultService(db.Factory, bank, profile, notifier);
        var ctrl = new VaultController(service, new VaultWsHub())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };

        return new Host
        {
            Controller = ctrl,
            Notifier = notifier,
            Resources = [db, sp]
        };
    }

    private const string Uuid = TestConstants.Uuid;
    private const string Uuid2 = TestConstants.BorrowUuid;

    private static VaultDepositRequest Deposit(string uuid, decimal amount) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "dep", DisplayNote = "入金", Server = "dev"
    };

    private static VaultWithdrawRequest Withdraw(string uuid, decimal amount) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "wd", DisplayNote = "出金", Server = "dev"
    };

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<T>().Which;

    [Fact(DisplayName = "口座未作成時の残高は 0 / version 0")]
    public async Task GetBalance_NoAccount_ReturnsZero()
    {
        using var host = BuildController();
        var res = await host.Controller.GetBalance(Uuid);
        var bal = Ok(res);
        bal.Balance.Should().Be(0m);
        bal.Version.Should().Be(0L);
    }

    [Fact(DisplayName = "入金成功: 残高増加・version++・push 発行・ログ記録")]
    public async Task Deposit_Success()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var dep = Ok(await ctrl.Deposit(Deposit(Uuid, 500)));
        dep.Balance.Should().Be(500m);
        dep.Version.Should().Be(1L);

        // コミット後 push が確定残高+version で発行される
        host.Notifier.Pushes.Should().ContainSingle();
        host.Notifier.Pushes.TryPeek(out var change).Should().BeTrue();
        change!.Balance.Should().Be(500m);
        change.Version.Should().Be(1L);
        change.Cause.Should().Be("DEPOSIT");
        change.Uuid.Should().Be(Uuid);

        var bal = Ok(await ctrl.GetBalance(Uuid));
        bal.Balance.Should().Be(500m);
        bal.Version.Should().Be(1L);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs.Should().ContainSingle();
        logs[0].Amount.Should().Be(500m);
        logs[0].Deposit.Should().BeTrue();
    }

    [Fact(DisplayName = "入金失敗: 金額0はモデル検証で弾かれる")]
    public void Deposit_Invalid_FailsValidation()
    {
        using var host = BuildController();
        host.Controller.TryValidateModel(Deposit(Uuid, 0)).Should().BeFalse();
    }

    [Fact(DisplayName = "出金成功: 残高減少・version 連番")]
    public async Task Withdraw_Success()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 1000));
        var wd = Ok(await ctrl.Withdraw(Withdraw(Uuid, 600)));
        wd.Balance.Should().Be(400m);
        wd.Version.Should().Be(2L);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Amount.Should().Be(-600m);
        logs[0].Deposit.Should().BeFalse();
    }

    [Fact(DisplayName = "出金失敗: 残高不足は 409 で残高・version 不変・push なし")]
    public async Task Withdraw_Insufficient_409_NoChange()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 100));
        host.Notifier.Pushes.Clear();

        var ng = await ctrl.Withdraw(Withdraw(Uuid, 500));
        ng.Result.Should().BeOfType<ConflictObjectResult>();

        host.Notifier.Pushes.Should().BeEmpty();
        var bal = Ok(await ctrl.GetBalance(Uuid));
        bal.Balance.Should().Be(100m);
        bal.Version.Should().Be(1L);
    }

    [Fact(DisplayName = "送金成功: 送金元減少・送金先増加・両者へ push")]
    public async Task Transfer_Success_PushesBoth()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 1000));
        host.Notifier.Pushes.Clear();

        var req = new VaultTransferRequest
        {
            FromUuid = Uuid, ToUuid = Uuid2, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = "dev"
        };
        var from = Ok(await ctrl.Transfer(req));
        from.Balance.Should().Be(700m);

        var toBal = Ok(await ctrl.GetBalance(Uuid2));
        toBal.Balance.Should().Be(300m);

        // 送金元・送金先の双方へ push
        host.Notifier.Pushes.Should().HaveCount(2);
        host.Notifier.Pushes.Select(p => p.Uuid).Should().Contain([Uuid, Uuid2]);
        host.Notifier.Pushes.Should().OnlyContain(p => p.Cause == "TRANSFER");
    }

    [Fact(DisplayName = "送金失敗: 残高不足は 409 で双方不変")]
    public async Task Transfer_Insufficient_409()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 100));
        var req = new VaultTransferRequest
        {
            FromUuid = Uuid, ToUuid = Uuid2, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = "dev"
        };
        (await ctrl.Transfer(req)).Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await ctrl.GetBalance(Uuid2)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "set: 絶対値設定で差分をログ化し push 発行")]
    public async Task Set_AbsoluteValue()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 200));
        host.Notifier.Pushes.Clear();

        var req = new VaultSetRequest
        {
            Uuid = Uuid, Amount = 1000, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = "dev"
        };
        var set = Ok(await ctrl.Set(req));
        set.Balance.Should().Be(1000m);

        host.Notifier.Pushes.Should().ContainSingle();
        host.Notifier.Pushes.TryPeek(out var change).Should().BeTrue();
        change!.Cause.Should().Be("SET");
        change.Balance.Should().Be(1000m);

        // 差分 +800 がログに残る
        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Amount.Should().Be(800m);
    }

    [Fact(DisplayName = "set: 変化が無い場合は version 据え置き・push なし")]
    public async Task Set_NoChange_NoPush()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 500));
        host.Notifier.Pushes.Clear();

        var req = new VaultSetRequest
        {
            Uuid = Uuid, Amount = 500, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = "dev"
        };
        var set = Ok(await ctrl.Set(req));
        set.Balance.Should().Be(500m);
        set.Version.Should().Be(1L); // 入金時の version のまま
        host.Notifier.Pushes.Should().BeEmpty();
    }

    [Fact(DisplayName = "ensure: 口座を冪等作成し残高 0 を返す(push なし)")]
    public async Task Ensure_CreatesAccount()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var res = Ok(await ctrl.Ensure(Uuid));
        res.Balance.Should().Be(0m);
        host.Notifier.Pushes.Should().BeEmpty();

        // 2回目も成功(冪等)
        Ok(await ctrl.Ensure(Uuid)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "並列ランダム入出金: 最終残高がログ合計と一致し、409はログに出ない")]
    public async Task Concurrent_RandomOps_BalanceEqualsLogSum()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 2000));

        var rnd = new Random(54321);
        var operations = Enumerable.Range(0, 100)
            .Select(_ => (amount: (decimal)rnd.Next(1, 501), deposit: rnd.Next(0, 2) == 0))
            .ToArray();

        var conflictFlags = new ConcurrentBag<bool>();
        var tasks = operations.Select(async op =>
        {
            if (op.deposit)
            {
                (await ctrl.Deposit(Deposit(Uuid, op.amount))).Result.Should().BeOfType<OkObjectResult>();
                conflictFlags.Add(false);
            }
            else
            {
                var res = await ctrl.Withdraw(Withdraw(Uuid, op.amount));
                var isConflict = res.Result is ConflictObjectResult;
                if (!isConflict) res.Result.Should().BeOfType<OkObjectResult>();
                conflictFlags.Add(isConflict);
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        var logs = Ok(await ctrl.GetLogs(Uuid, 1000));
        var sum = logs.Sum(l => l.Amount);
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(sum);

        var withdrawAttempts = operations.Count(o => !o.deposit);
        var negativeLogs = logs.Count(l => !l.Deposit);
        var conflicts = conflictFlags.Count(f => f);
        (withdrawAttempts - negativeLogs).Should().Be(conflicts);
    }
}
