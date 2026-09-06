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
using System.Collections.Concurrent;

namespace Test.Controllers;

// 電子マネー(Vault)コントローラの単体テスト(SQLite)。
// session claim(単一書き込み者)・冪等キー・残高上限・deposit/withdraw/transfer/set/ensure/logs を検証する。
public class VaultControllerTests
{
    private sealed class Host : IDisposable
    {
        public required VaultController Controller { get; init; }
        public required VaultSessionManager Sessions { get; init; }
        public required FakeVaultSessionChannel Channel { get; init; }
        public required List<IDisposable> Resources { get; init; }

        public void Dispose()
        {
            foreach (var d in Resources)
            {
                try { d.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    private static Host BuildController(decimal? maxBalance = null)
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
        var service = new VaultService(db.Factory, bank, profile, sessions, channel, config);
        var ctrl = new VaultController(service, new VaultWsHub(sessions))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };

        return new Host
        {
            Controller = ctrl,
            Sessions = sessions,
            Channel = channel,
            Resources = [db, sp]
        };
    }

    private const string Uuid = TestConstants.Uuid;
    private const string Uuid2 = TestConstants.BorrowUuid;
    private const string Server = "dev";

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<T>().Which;

    // claim して sessionId を得る(書き込みの前提条件)。
    private static async Task<string> ClaimAsync(Host host, string uuid, string server = Server)
    {
        var res = Ok(await host.Controller.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = uuid, Player = "test-player", Server = server
        }));
        return res.SessionId;
    }

    private static VaultDepositRequest Deposit(string uuid, decimal amount, string sessionId, string? operationId = null) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "dep", DisplayNote = "入金", Server = Server,
        SessionId = sessionId, OperationId = operationId
    };

    private static VaultWithdrawRequest Withdraw(string uuid, decimal amount, string sessionId, string? operationId = null) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "wd", DisplayNote = "出金", Server = Server,
        SessionId = sessionId, OperationId = operationId
    };

    [Fact(DisplayName = "口座未作成時の残高は 0 / version 0")]
    public async Task GetBalance_NoAccount_ReturnsZero()
    {
        using var host = BuildController();
        var res = await host.Controller.GetBalance(Uuid);
        var bal = Ok(res);
        bal.Balance.Should().Be(0m);
        bal.Version.Should().Be(0L);
    }

    [Fact(DisplayName = "claim: 口座を冪等作成し sessionId と残高上限(既定1兆)を配布する")]
    public async Task Claim_CreatesAccount_ReturnsSessionAndMaxBalance()
    {
        using var host = BuildController();
        var res = Ok(await host.Controller.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = Uuid, Player = "test-player", Server = Server
        }));

        res.SessionId.Should().NotBeNullOrEmpty();
        res.Balance.Should().Be(0m);
        res.Version.Should().Be(0L);
        res.MaxBalance.Should().Be(1_000_000_000_000m);

        var status = Ok(host.Controller.GetSession(Uuid));
        status.Online.Should().BeTrue();
        status.Server.Should().Be(Server);
        status.SessionId.Should().Be(res.SessionId);
    }

    [Fact(DisplayName = "claim: 同期チャネル未接続のサーバーは 409")]
    public async Task Claim_ServerNotConnected_409()
    {
        using var host = BuildController();
        host.Channel.Connected = false;

        var res = await host.Controller.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = Uuid, Player = "test-player", Server = Server
        });
        res.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "claim: 後勝ちで旧 session が失効し、旧サーバーへ失効通知が飛ぶ")]
    public async Task Claim_LastWriteWins_NotifiesOldServer()
    {
        using var host = BuildController();
        var oldSession = await ClaimAsync(host, Uuid, "dev");
        _ = await ClaimAsync(host, Uuid, "dev2");

        host.Channel.Revocations.Should().ContainSingle();
        host.Channel.Revocations.TryPeek(out var rev).Should().BeTrue();
        rev.Server.Should().Be("dev");
        rev.Uuid.Should().Be(Uuid);
        rev.SessionId.Should().Be(oldSession);
        rev.NewServer.Should().Be("dev2");

        // 旧 sessionId での書き込みは SessionMismatch(409)
        (await host.Controller.Deposit(Deposit(Uuid, 100, oldSession)))
            .Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "release: 解除後の書き込みは 409、session 状態は offline になる")]
    public async Task Release_ThenWriteFails()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);

        (await host.Controller.ReleaseSession(new VaultSessionReleaseRequest
        {
            Uuid = Uuid, SessionId = session, Server = Server
        })).Should().BeOfType<NoContentResult>();

        Ok(host.Controller.GetSession(Uuid)).Online.Should().BeFalse();
        (await host.Controller.Deposit(Deposit(Uuid, 100, session)))
            .Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "入金成功: 残高増加・version++・ログ記録")]
    public async Task Deposit_Success()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        var dep = Ok(await ctrl.Deposit(Deposit(Uuid, 500, session)));
        dep.Balance.Should().Be(500m);
        dep.Version.Should().Be(1L);

        var bal = Ok(await ctrl.GetBalance(Uuid));
        bal.Balance.Should().Be(500m);
        bal.Version.Should().Be(1L);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs.Should().ContainSingle();
        logs[0].Amount.Should().Be(500m);
        logs[0].Deposit.Should().BeTrue();
    }

    [Fact(DisplayName = "入金失敗: claim なし(sessionId 不一致)は 409 で残高不変")]
    public async Task Deposit_NoClaim_409()
    {
        using var host = BuildController();

        var res = await host.Controller.Deposit(Deposit(Uuid, 500, "no-such-session"));
        res.Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await host.Controller.GetBalance(Uuid)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "入金失敗: 別サーバーからの書き込み(server 不一致)は 409")]
    public async Task Deposit_WrongServer_409()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid, "dev2");

        // sessionId は正しいが Server が claim と不一致
        var req = Deposit(Uuid, 500, session); // Server = "dev"
        (await host.Controller.Deposit(req)).Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "入金失敗: 金額0はモデル検証で弾かれる")]
    public void Deposit_Invalid_FailsValidation()
    {
        using var host = BuildController();
        host.Controller.TryValidateModel(Deposit(Uuid, 0, "s")).Should().BeFalse();
    }

    [Fact(DisplayName = "冪等リプレイ: 同一 operationId の再送は同じ結果を返し二重適用しない")]
    public async Task Deposit_SameOperationId_ReplaysStoredResult()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;
        const string opId = "0a1b2c3d-op-000000000001";

        var first = Ok(await ctrl.Deposit(Deposit(Uuid, 500, session, opId)));
        // 間に別の取引を挟んでも、リプレイは初回時点の結果を返す
        await ctrl.Deposit(Deposit(Uuid, 100, session));
        var replay = Ok(await ctrl.Deposit(Deposit(Uuid, 500, session, opId)));

        replay.Balance.Should().Be(first.Balance);
        replay.Version.Should().Be(first.Version);

        // 適用は1回だけ(残高 600、opId 付きログは1件)
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(600m);
        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs.Should().HaveCount(2);
    }

    [Fact(DisplayName = "出金成功: 残高減少・version 連番・冪等リプレイも同結果")]
    public async Task Withdraw_Success_AndReplay()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;
        const string opId = "0a1b2c3d-op-000000000002";

        await ctrl.Deposit(Deposit(Uuid, 1000, session));
        var wd = Ok(await ctrl.Withdraw(Withdraw(Uuid, 600, session, opId)));
        wd.Balance.Should().Be(400m);
        wd.Version.Should().Be(2L);

        var replay = Ok(await ctrl.Withdraw(Withdraw(Uuid, 600, session, opId)));
        replay.Balance.Should().Be(400m);
        replay.Version.Should().Be(2L);
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(400m);

        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Amount.Should().Be(-600m);
        logs[0].Deposit.Should().BeFalse();
    }

    [Fact(DisplayName = "出金失敗: 残高不足は 409 で残高・version 不変")]
    public async Task Withdraw_Insufficient_409_NoChange()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 100, session));

        var ng = await ctrl.Withdraw(Withdraw(Uuid, 500, session));
        ng.Result.Should().BeOfType<ConflictObjectResult>();

        var bal = Ok(await ctrl.GetBalance(Uuid));
        bal.Balance.Should().Be(100m);
        bal.Version.Should().Be(1L);
    }

    [Fact(DisplayName = "残高上限: 操作金額・更新後残高が Vault:MaxBalance を超えると 409")]
    public async Task Deposit_MaxBalance_409()
    {
        using var host = BuildController(maxBalance: 1000m);
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        // 操作金額が上限超え
        (await ctrl.Deposit(Deposit(Uuid, 1001, session))).Result.Should().BeOfType<ConflictObjectResult>();

        // 更新後残高が上限超え
        Ok(await ctrl.Deposit(Deposit(Uuid, 600, session))).Balance.Should().Be(600m);
        (await ctrl.Deposit(Deposit(Uuid, 600, session))).Result.Should().BeOfType<ConflictObjectResult>();
        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(600m);

        // 上限は claim 応答で配布される
        var claim = Ok(await ctrl.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = Uuid2, Player = "p2", Server = Server
        }));
        claim.MaxBalance.Should().Be(1000m);
    }

    [Fact(DisplayName = "設定不正: Vault:MaxBalance が 0 以下なら全APIが 500 で fail-closed")]
    public async Task InvalidConfig_FailsClosed()
    {
        using var host = BuildController(maxBalance: 0m);

        var bal = await host.Controller.GetBalance(Uuid);
        bal.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);

        var claim = await host.Controller.ClaimSession(new VaultSessionClaimRequest
        {
            Uuid = Uuid, Player = "p", Server = Server
        });
        claim.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact(DisplayName = "送金成功: 両者の claim 必須で、両残高+version を返す")]
    public async Task Transfer_Success_ReturnsBothBalances()
    {
        using var host = BuildController();
        var fromSession = await ClaimAsync(host, Uuid);
        var toSession = await ClaimAsync(host, Uuid2);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 1000, fromSession));

        var req = new VaultTransferRequest
        {
            FromUuid = Uuid, ToUuid = Uuid2, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = Server,
            FromSessionId = fromSession, ToSessionId = toSession
        };
        var res = Ok(await ctrl.Transfer(req));
        res.FromBalance.Should().Be(700m);
        res.ToBalance.Should().Be(300m);
        res.ToVersion.Should().Be(1L);

        Ok(await ctrl.GetBalance(Uuid2)).Balance.Should().Be(300m);
    }

    [Fact(DisplayName = "送金失敗: 送金先の claim が無ければ 409 で双方不変")]
    public async Task Transfer_NoToClaim_409()
    {
        using var host = BuildController();
        var fromSession = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 1000, fromSession));

        var req = new VaultTransferRequest
        {
            FromUuid = Uuid, ToUuid = Uuid2, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = Server,
            FromSessionId = fromSession, ToSessionId = "no-such-session"
        };
        (await ctrl.Transfer(req)).Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(1000m);
        Ok(await ctrl.GetBalance(Uuid2)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "送金失敗: 残高不足は 409 で双方不変")]
    public async Task Transfer_Insufficient_409()
    {
        using var host = BuildController();
        var fromSession = await ClaimAsync(host, Uuid);
        var toSession = await ClaimAsync(host, Uuid2);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 100, fromSession));
        var req = new VaultTransferRequest
        {
            FromUuid = Uuid, ToUuid = Uuid2, Amount = 300,
            PluginName = "test", Note = "pay", DisplayNote = "送金", Server = Server,
            FromSessionId = fromSession, ToSessionId = toSession
        };
        (await ctrl.Transfer(req)).Result.Should().BeOfType<ConflictObjectResult>();

        Ok(await ctrl.GetBalance(Uuid)).Balance.Should().Be(100m);
        Ok(await ctrl.GetBalance(Uuid2)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "set: claim 必須の絶対値設定で差分をログ化する")]
    public async Task Set_AbsoluteValue()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 200, session));

        var req = new VaultSetRequest
        {
            Uuid = Uuid, Amount = 1000, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = Server,
            SessionId = session
        };
        var set = Ok(await ctrl.Set(req));
        set.Balance.Should().Be(1000m);

        // 差分 +800 がログに残る
        var logs = Ok(await ctrl.GetLogs(Uuid, 10));
        logs[0].Amount.Should().Be(800m);
    }

    [Fact(DisplayName = "set: claim なしは 409、変化なしは version 据え置き")]
    public async Task Set_NoClaim409_NoChangeKeepsVersion()
    {
        using var host = BuildController();

        (await host.Controller.Set(new VaultSetRequest
        {
            Uuid = Uuid, Amount = 100, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = Server,
            SessionId = "no-such-session"
        })).Result.Should().BeOfType<ConflictObjectResult>();

        var session = await ClaimAsync(host, Uuid);
        await host.Controller.Deposit(Deposit(Uuid, 500, session));

        var set = Ok(await host.Controller.Set(new VaultSetRequest
        {
            Uuid = Uuid, Amount = 500, PluginName = "admin", Note = "set", DisplayNote = "設定", Server = Server,
            SessionId = session
        }));
        set.Balance.Should().Be(500m);
        set.Version.Should().Be(1L); // 入金時の version のまま
    }

    [Fact(DisplayName = "ensure: 口座を冪等作成し残高 0 を返す(claim 不要)")]
    public async Task Ensure_CreatesAccount()
    {
        using var host = BuildController();
        var ctrl = host.Controller;

        var res = Ok(await ctrl.Ensure(Uuid));
        res.Balance.Should().Be(0m);

        // 2回目も成功(冪等)
        Ok(await ctrl.Ensure(Uuid)).Balance.Should().Be(0m);
    }

    [Fact(DisplayName = "並列ランダム入出金: 最終残高がログ合計と一致し、409はログに出ない")]
    public async Task Concurrent_RandomOps_BalanceEqualsLogSum()
    {
        using var host = BuildController();
        var session = await ClaimAsync(host, Uuid);
        var ctrl = host.Controller;

        await ctrl.Deposit(Deposit(Uuid, 2000, session));

        var rnd = new Random(54321);
        var operations = Enumerable.Range(0, 100)
            .Select(_ => (amount: (decimal)rnd.Next(1, 501), deposit: rnd.Next(0, 2) == 0))
            .ToArray();

        var conflictFlags = new ConcurrentBag<bool>();
        var tasks = operations.Select(async op =>
        {
            if (op.deposit)
            {
                (await ctrl.Deposit(Deposit(Uuid, op.amount, session))).Result.Should().BeOfType<OkObjectResult>();
                conflictFlags.Add(false);
            }
            else
            {
                var res = await ctrl.Withdraw(Withdraw(Uuid, op.amount, session));
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
