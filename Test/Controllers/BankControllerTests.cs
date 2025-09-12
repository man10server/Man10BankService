using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;
using System.Collections.Concurrent;

namespace Test.Controllers;

public class BankControllerTests
{
    private static ControllerHost BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(BankController).Assembly);
        var sp = services.BuildServiceProvider();

        var service = new BankService(db.Factory);
        var ctrl = new BankController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        return new ControllerHost
        {
            Controller = ctrl,
            Resources = [db, sp]
        };
    }

    [Fact(DisplayName = "DBダウン時: 残高取得は500エラー")]
    public async Task GetBalance_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;

        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.GetBalance(TestConstants.Uuid);
        (res.Result as ObjectResult)!.StatusCode.Should().Be(500);
        (res.Result as ObjectResult)!.Value.Should().BeOfType<ProblemDetails>();
    }
    
    [Fact(DisplayName = "入金成功: 残高が増加しログが記録される")]
    public async Task Deposit_Success_ShouldIncreaseBalance_AndWriteLog()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        var req = new DepositRequest
        {
            Uuid = TestConstants.Uuid,
            Amount = 500,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();
        var result = await ctrl.Deposit(req);
        var deposited = result.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        deposited.Should().Be(500m);

        var balRes = await ctrl.GetBalance(req.Uuid);
        var balance = balRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        balance.Should().Be(500m);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10);
        var logs = logsRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = 500m,
            Deposit = true,
            TestConstants.Uuid,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        });
    }
    
    [Fact(DisplayName = "入金失敗: 金額不正残高とログは変化なし")]
    public async Task Deposit_Invalid_ShouldNotChangeBalance_AndReturn400()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        var req = new DepositRequest
        {
            Uuid = TestConstants.Uuid,
            Amount = 0,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeFalse();
        
        var result = await ctrl.Deposit(req);
        var bad = result.Result.Should().BeOfType<ObjectResult>().Which;
        bad.Value.Should().BeOfType<ValidationProblemDetails>();

        var balRes = await ctrl.GetBalance(req.Uuid);
        var bal = balRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        bal.Should().Be(0m);

        var logsRes = await ctrl.GetLogs(req.Uuid, 10);
        var logs = logsRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        logs.Count.Should().Be(0);
    }

    [Fact(DisplayName = "入金失敗: 入金は500エラー")]
    public async Task Deposit_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;

        var req = new DepositRequest
        {
            Uuid = TestConstants.Uuid,
            Amount = 100,
            PluginName = "test",
            Note = "down",
            DisplayNote = "DBダウン",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        // DB をダウン
        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res = await ctrl.Deposit(req);
        (res.Result as ObjectResult)!.StatusCode.Should().Be(500);
        (res.Result as ObjectResult)!.Value.Should().BeOfType<ProblemDetails>();
    }
    
    [Fact(DisplayName = "出金成功: 残高が減少しログが記録される")]
    public async Task Withdraw_Success_ShouldDecreaseBalance_AndWriteLog()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        const string uuid = TestConstants.Uuid;

        await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Amount = 1000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        });

        var withdraw = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Amount = 600,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        });
        withdraw.Result.Should().BeOfType<OkObjectResult>();

        var balRes2 = await ctrl.GetBalance(uuid);
        var bal2 = balRes2.Result
            .Should().BeOfType<OkObjectResult>().Which.Value!
            .Should().BeOfType<decimal>().Which;
        bal2.Should().Be(400m);

        var logsRes2 = await ctrl.GetLogs(uuid, 10);
        var logs = logsRes2.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = -600m,
            Deposit = false,
            Uuid = uuid,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        });
    }

    [Fact(DisplayName = "出金成功後の残高不足: 2回目は409で残高不変")]
    public async Task Withdraw_Success_Then_Insufficient_Should409_And_NoChange()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        const string uuid = TestConstants.Uuid;

        await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Amount = 1000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        });

        var withdraw = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Amount = 600,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        });
        withdraw.Result.Should().BeOfType<OkObjectResult>();
        
        var firstBalResult = await ctrl.GetBalance(uuid);
        var firstBal = firstBalResult.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        firstBal.Should().Be(400m);

        var ng = await ctrl.Withdraw(new WithdrawRequest
        {
            Uuid = uuid,
            Amount = 500,
            PluginName = "test",
            Note = "w2",
            DisplayNote = "出金2",
            Server = "dev"
        });
        ng.Result.Should().BeOfType<ConflictObjectResult>();

        var secondBalResult = await ctrl.GetBalance(uuid);
        var secondBal = secondBalResult.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        secondBal.Should().Be(400m);
    }


    [Fact(DisplayName = "DBダウン時: 出金は500エラー")]
    public async Task Withdraw_DbDown_ShouldReturn500()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;

        var req = new WithdrawRequest
        {
            Uuid = TestConstants.Uuid,
            Amount = 100,
            PluginName = "test",
            Note = "down",
            DisplayNote = "DBダウン",
            Server = "dev"
        };
        ctrl.TryValidateModel(req).Should().BeTrue();

        // DB をダウン
        var db = host.Resources.OfType<TestDbFactory>().First();
        db.Connection.Close();

        var res2 = await ctrl.Withdraw(req);
        (res2.Result as ObjectResult)!.StatusCode.Should().Be(500);
        (res2.Result as ObjectResult)!.Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact(DisplayName = "MoneyLog取得: 100件の入金後に limit/offset で中間10件を取得")]
    public async Task GetLogs_Pagination_ShouldReturnMiddleSlice()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        const string uuid = TestConstants.Uuid;

        for (var i = 1; i <= 100; i++)
        {
            var req = new DepositRequest
            {
                Uuid = uuid,
                Amount = i,
                PluginName = "test",
                Note = $"dep{i}",
                DisplayNote = $"入金{i}",
                Server = "dev"
            };
            ctrl.TryValidateModel(req).Should().BeTrue();
            var res = await ctrl.Deposit(req);
            res.Result.Should().BeOfType<OkObjectResult>();
        }

        var get = await ctrl.GetLogs(uuid, limit: 10);
        var top = get.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        top.First().Amount.Should().Be(100);

        var midRes = await ctrl.GetLogs(uuid, limit: 10, offset: 30);
        var logs = midRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        logs.Should().HaveCount(10);
        var amounts = logs.Select(x => x.Amount).ToArray();
        amounts.Should().BeEquivalentTo(new decimal[] { 70, 69, 68, 67, 66, 65, 64, 63, 62, 61 }, opt => opt.WithStrictOrdering());
    }

    [Fact(DisplayName = "並列ランダム入出金: 最終残高がログ合計と一致し、409はログに出ない")]
    public async Task Concurrent_RandomOps_ShouldBalanceEqualLogSum_And409NoLog()
    {
        using var host = BuildController();
        var ctrl = (BankController)host.Controller;
        const string uuid = TestConstants.Uuid;

        // 初期残高を作って過剰な409を減らす
        (await ctrl.Deposit(new DepositRequest
        {
            Uuid = uuid,
            Amount = 2000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        })).Result.Should().BeOfType<OkObjectResult>();

        var rnd = new Random(12345);
        var operations = Enumerable.Range(0, 100)
            .Select(_ => (amount: (decimal)rnd.Next(1, 501), deposit: rnd.Next(0, 2) == 0))
            .ToArray();

        var conflictFlags = new ConcurrentBag<bool>();
        var tasks = operations.Select(async op =>
        {
            if (op.deposit)
            {
                var res = await ctrl.Deposit(new DepositRequest
                {
                    Uuid = uuid,
                    Amount = op.amount,
                    PluginName = "test",
                    Note = $"dep{op.amount}",
                    DisplayNote = $"入金{op.amount}",
                    Server = "dev"
                });
                res.Result.Should().BeOfType<OkObjectResult>();
                conflictFlags.Add(false);
            }
            else
            {
                var res = await ctrl.Withdraw(new WithdrawRequest
                {
                    Uuid = uuid,
                    Amount = op.amount,
                    PluginName = "test",
                    Note = $"wd{op.amount}",
                    DisplayNote = $"出金{op.amount}",
                    Server = "dev"
                });
                // 成功か409のいずれか
                var isConflict = res.Result is ConflictObjectResult;
                if (!isConflict)
                {
                    res.Result.Should().BeOfType<OkObjectResult>();
                }
                conflictFlags.Add(isConflict);
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        var get = await ctrl.GetLogs(uuid, limit: 1000);
        var logs = get.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<MoneyLog>>().Which;
        var sum = logs.Sum(l => l.Amount);

        var balRes3 = await ctrl.GetBalance(uuid);
        var balance = balRes3.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<decimal>().Which;
        balance.Should().Be(sum);

        logs.All(l => l.Amount >= 0m == l.Deposit).Should().BeTrue();

        var withdrawAttempts = operations.Count(o => !o.deposit);
        var negativeLogs = logs.Count(l => !l.Deposit);
        var conflicts = conflictFlags.Count(f => f);
        (withdrawAttempts - negativeLogs).Should().Be(conflicts);
    }
}
