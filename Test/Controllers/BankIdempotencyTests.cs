using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
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

// 冪等キー(requestId)の検証。タイムアウト等で結果不明になった要求をプラグインが同じ requestId で
// 再送したとき、二重実行されず初回の結果が返ること、失敗した要求のキーは消費されないことを確認する。
public class BankIdempotencyTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string OtherUuid = "49c42256-2357-4963-8678-7a06e6dd3125";
    private const string RequestId = "0f3b2a3c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

    private sealed record TestEnv(ControllerHost Host, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static (TestEnv Env, BankController Ctrl) Build()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();

        var service = new BankService(db.Factory, new FakePlayerProfileService());
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        var host = new ControllerHost { Controller = ctrl, Resources = [db, sp] };
        return (new TestEnv(host, db.Factory), ctrl);
    }

    private static DepositRequest Deposit(decimal amount, string? requestId, string uuid = Uuid) => new()
    {
        Uuid = uuid, Amount = amount, PluginName = "test", Note = "dep", DisplayNote = "入金", Server = "dev",
        RequestId = requestId
    };

    private static WithdrawRequest Withdraw(decimal amount, string? requestId) => new()
    {
        Uuid = Uuid, Amount = amount, PluginName = "test", Note = "wd", DisplayNote = "出金", Server = "dev",
        RequestId = requestId
    };

    private static TransferRequest Transfer(decimal amount, string? requestId) => new()
    {
        FromUuid = Uuid, ToUuid = OtherUuid, Amount = amount, PluginName = "test",
        Note = "tr", DisplayNote = "送金", Server = "dev", RequestId = requestId
    };

    private static decimal BalanceOf(ActionResult<BalanceResponse> res) =>
        res.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<BalanceResponse>().Which.Balance;

    private static async Task<int> LogCountAsync(IDbContextFactory<BankDbContext> f, string uuid)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.MoneyLogs.AsNoTracking().CountAsync(x => x.Uuid == uuid);
    }

    [Fact(DisplayName = "冪等: 同じrequestIdの入金を再送しても残高は1回分しか増えず、初回と同じ残高が返る")]
    public async Task Deposit_SameRequestId_ShouldApplyOnce()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);
        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);

        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(500m);
        (await LogCountAsync(env.DbFactory, Uuid)).Should().Be(1);
    }

    [Fact(DisplayName = "冪等: 同じrequestIdの出金を再送しても1回分しか減らない")]
    public async Task Withdraw_SameRequestId_ShouldApplyOnce()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        BalanceOf(await ctrl.Deposit(Deposit(1000m, null))).Should().Be(1000m);

        BalanceOf(await ctrl.Withdraw(Withdraw(300m, RequestId))).Should().Be(700m);
        BalanceOf(await ctrl.Withdraw(Withdraw(300m, RequestId))).Should().Be(700m);

        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(700m);
        (await LogCountAsync(env.DbFactory, Uuid)).Should().Be(2);
    }

    [Fact(DisplayName = "冪等: 同じrequestIdの送金を再送しても両者の残高は1回分しか動かない")]
    public async Task Transfer_SameRequestId_ShouldApplyOnce()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        BalanceOf(await ctrl.Deposit(Deposit(1000m, null))).Should().Be(1000m);

        BalanceOf(await ctrl.Transfer(Transfer(300m, RequestId))).Should().Be(700m);
        BalanceOf(await ctrl.Transfer(Transfer(300m, RequestId))).Should().Be(700m);

        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(700m);
        BalanceOf(await ctrl.GetBalance(OtherUuid)).Should().Be(300m);
        (await LogCountAsync(env.DbFactory, OtherUuid)).Should().Be(1);
    }

    [Fact(DisplayName = "冪等: 初回結果は後続の取引に影響されず、再送時はその時点の残高ではなく初回の残高を返す")]
    public async Task Replay_ShouldReturnOriginalBalance_NotCurrent()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);
        BalanceOf(await ctrl.Deposit(Deposit(100m, null))).Should().Be(600m);

        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);
        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(600m);
    }

    [Fact(DisplayName = "冪等: 同じrequestIdで金額の異なる要求は409(IdempotencyKeyMismatch)で拒否され残高は変わらない")]
    public async Task SameRequestId_DifferentPayload_ShouldReturn409()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);

        var res = await ctrl.Deposit(Deposit(999m, RequestId));
        res.Result.Should().BeOfType<ConflictObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>()
            .Which.Extensions["code"].Should().Be(ErrorCode.IdempotencyKeyMismatch.ToString());

        var asWithdraw = await ctrl.Withdraw(Withdraw(500m, RequestId));
        asWithdraw.Result.Should().BeOfType<ConflictObjectResult>();

        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(500m);
    }

    [Fact(DisplayName = "冪等: 失敗(残高不足)した要求のrequestIdは消費されず、条件が整えば同じキーで成功する")]
    public async Task FailedRequest_ShouldNotConsumeKey()
    {
        var (env, ctrl) = Build();
        using var _ = env;
        BalanceOf(await ctrl.Deposit(Deposit(100m, null))).Should().Be(100m);

        (await ctrl.Withdraw(Withdraw(500m, RequestId))).Result.Should().BeOfType<ConflictObjectResult>();

        BalanceOf(await ctrl.Deposit(Deposit(1000m, null))).Should().Be(1100m);
        BalanceOf(await ctrl.Withdraw(Withdraw(500m, RequestId))).Should().Be(600m);
        BalanceOf(await ctrl.Withdraw(Withdraw(500m, RequestId))).Should().Be(600m);
    }

    [Fact(DisplayName = "冪等: requestIdは大文字小文字を区別せず同一キーとして扱う")]
    public async Task RequestId_ShouldBeCaseInsensitive()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId))).Should().Be(500m);
        BalanceOf(await ctrl.Deposit(Deposit(500m, RequestId.ToUpperInvariant()))).Should().Be(500m);
        BalanceOf(await ctrl.GetBalance(Uuid)).Should().Be(500m);
    }

    [Fact(DisplayName = "互換: requestIdなしの要求は従来どおり毎回実行される")]
    public async Task WithoutRequestId_ShouldApplyEveryTime()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        BalanceOf(await ctrl.Deposit(Deposit(500m, null))).Should().Be(500m);
        BalanceOf(await ctrl.Deposit(Deposit(500m, null))).Should().Be(1000m);
    }

    [Fact(DisplayName = "検証: UUID形式でないrequestIdはモデル検証で弾かれる")]
    public void InvalidRequestId_ShouldFailModelValidation()
    {
        var (env, ctrl) = Build();
        using var _ = env;

        ctrl.TryValidateModel(Deposit(500m, "not-a-uuid")).Should().BeFalse();
        ctrl.ModelState.Clear();
        ctrl.TryValidateModel(Deposit(500m, RequestId)).Should().BeTrue();
        ctrl.ModelState.Clear();
        ctrl.TryValidateModel(Deposit(500m, null)).Should().BeTrue();
    }
}
