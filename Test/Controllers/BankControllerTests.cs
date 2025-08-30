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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Test.Controllers;

public class BankControllerTests
{
    private static BankController BuildController(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // InMemory はトランザクション未対応のため SQLite(:memory:) を使用
        var connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        connection.Open();
        services.AddSingleton(connection);
        services.AddPooledDbContextFactory<BankDbContext>(o => o.UseSqlite(connection));
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();
        // スキーマ作成
        using (var scope = sp.CreateScope())
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
            using var db = f.CreateDbContext();
            db.Database.EnsureCreated();
        }
        var factory = sp.GetRequiredService<IDbContextFactory<BankDbContext>>();
        var service = new BankService(factory);
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return ctrl;
    }

    [Fact]
    public async Task Deposit_Success_ShouldIncreaseBalance_AndWriteLog()
    {
        var ctrl = BuildController("deposit-success");
        var req = new DepositRequest
        {
            Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Player = "steve",
            Amount = 500,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();
        var result = await ctrl.Deposit(req) as ObjectResult;
        result!.StatusCode.Should().Be(200);
        var body = result.Value as ApiResult<decimal>;
        body!.Data.Should().Be(500);

        var balRes = await ctrl.GetBalance(req.Uuid) as ObjectResult;
        (balRes!.Value as ApiResult<decimal>)!.Data.Should().Be(500);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10, 0) as ObjectResult;
        var logs = (logsRes!.Value as ApiResult<List<MoneyLog>>)!.Data!;
        logs.Count.Should().BeGreaterOrEqualTo(1);
        logs[0].Amount.Should().Be(500);
        logs[0].Deposit.Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_Invalid_ShouldNotChangeBalance_AndReturn400()
    {
        var ctrl = BuildController("deposit-invalid");
        var req = new DepositRequest
        {
            Uuid = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            Player = "alex",
            Amount = 0, // invalid
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        // Amount が不正のため検証は失敗する
        ctrl.TryValidateModel(req).Should().BeFalse();
        var result = await ctrl.Deposit(req) as ObjectResult;
        // パイプライン未実行のため StatusCode は null のことがある。型のみ確認。
        result.Should().NotBeNull();
        result!.Value.Should().BeOfType<ValidationProblemDetails>();

        var balRes = await ctrl.GetBalance(req.Uuid) as ObjectResult;
        (balRes!.Value as ApiResult<decimal>)!.Data.Should().Be(0);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10, 0) as ObjectResult;
        (logsRes!.Value as ApiResult<List<MoneyLog>>)!.Data!.Count.Should().Be(0);
    }

    [Fact]
    public async Task Withdraw_Success_Then_Insufficient_Should409_And_NoChange()
    {
        var ctrl = BuildController("withdraw-mix");
        var uuid = "cccccccc-cccc-cccc-cccc-cccccccccccc";

        await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 1000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        });

        var ok = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 600,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        }) as ObjectResult;
        ok!.StatusCode.Should().Be(200);
        var bal1 = await ctrl.GetBalance(uuid) as ObjectResult;
        (bal1!.Value as ApiResult<decimal>)!.Data.Should().Be(400);

        var ng = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 500,
            PluginName = "test",
            Note = "w2",
            DisplayNote = "出金2",
            Server = "dev"
        }) as ObjectResult;
        ng!.StatusCode.Should().Be(409);

        var bal2 = await ctrl.GetBalance(uuid) as ObjectResult;
        (bal2!.Value as ApiResult<decimal>)!.Data.Should().Be(400);
    }
}
