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
using System.Collections.Concurrent;
using Test.Infrastructure;

namespace Test.Controllers;

public class ChequesControllerTests
{
    private static ControllerHost BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(ChequesController).Assembly);
        var sp = services.BuildServiceProvider();

        var bank = new BankService(db.Factory);
        var service = new ChequeService(db.Factory, bank);
        var ctrl = new ChequesController(service)
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

    [Fact(DisplayName = "小切手作成→参照: 成功し未使用で返る")]
    public async Task Create_And_Get_Success()
    {
        using var host = BuildController();
        var ctrl = (ChequesController)host.Controller;

        var create = new ChequeCreateRequest
        {
            Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Player = "alex",
            Amount = 500,
            Note = "gift"
        };
        ctrl.TryValidateModel(create).Should().BeTrue();
        // 残高を用意
        var db = host.Resources.OfType<TestDbFactory>().First();
        var bank = new BankService(db.Factory);
        (await bank.DepositAsync(new DepositRequest
        {
            Uuid = create.Uuid,
            Player = create.Player,
            Amount = create.Amount,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        })).StatusCode.Should().Be(200);

        var post = await ctrl.Create(create) as ObjectResult;
        post!.StatusCode.Should().Be(200);
        var created = (post.Value as ApiResult<Cheque>)!.Data!;
        created.Id.Should().BeGreaterThan(0);
        created.Uuid.Should().Be(create.Uuid);
        created.Player.Should().Be("alex");
        created.Amount.Should().Be(500);
        created.Used.Should().BeFalse();

        var get = await ctrl.Get(created.Id) as ObjectResult;
        get!.StatusCode.Should().Be(200);
        var fetched = (get.Value as ApiResult<Cheque>)!.Data!;
        fetched.Id.Should().Be(created.Id);
        fetched.Used.Should().BeFalse();
    }

    [Fact(DisplayName = "小切手使用→再使用: 1回目成功・2回目は409で拒否")]
    public async Task Use_Success_Then_Conflict()
    {
        using var host = BuildController();
        var ctrl = (ChequesController)host.Controller;

        // 作成
        var create = new ChequeCreateRequest
        {
            Uuid = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            Player = "alex",
            Amount = 300,
            Note = "gift"
        };
        // 残高を用意
        var db2 = host.Resources.OfType<TestDbFactory>().First();
        var bank2 = new BankService(db2.Factory);
        (await bank2.DepositAsync(new DepositRequest
        {
            Uuid = create.Uuid,
            Player = create.Player,
            Amount = create.Amount,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        })).StatusCode.Should().Be(200);

        var createdRes = await ctrl.Create(create) as ObjectResult;
        createdRes!.StatusCode.Should().Be(200);
        var id = (createdRes.Value as ApiResult<Cheque>)!.Data!.Id;

        // 1回目使用
        var ok = await ctrl.Use(id, new ChequeUseRequest { Uuid = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", Player = "alex" }) as ObjectResult;
        ok!.StatusCode.Should().Be(200);
        var used = (ok.Value as ApiResult<Cheque>)!.Data!;
        used.Used.Should().BeTrue();
        used.UsePlayer.Should().Be("alex");

        // 2回目（別プレイヤー）→ 409
        var ng = await ctrl.Use(id, new ChequeUseRequest { Uuid = "ffffffff-ffff-ffff-ffff-ffffffffffff", Player = "steve" }) as ObjectResult;
        ng!.StatusCode.Should().Be(409);

        // 参照
        var get = await ctrl.Get(id) as ObjectResult;
        var current = (get!.Value as ApiResult<Cheque>)!.Data!;
        current.Used.Should().BeTrue();
        current.UsePlayer.Should().Be("alex");
    }

    [Fact(DisplayName = "小切手参照/使用: 存在しないIDは404")]
    public async Task Get_And_Use_NotFound()
    {
        using var host = BuildController();
        var ctrl = (ChequesController)host.Controller;

        (await ctrl.Get(999999) as ObjectResult)!.StatusCode.Should().Be(404);
        (await ctrl.Use(999999, new ChequeUseRequest { Uuid = "11111111-1111-1111-1111-111111111111", Player = "alex" }) as ObjectResult)!.StatusCode.Should().Be(404);
    }

    [Fact(DisplayName = "小切手作成: 金額0は400で拒否")]
    public async Task Create_InvalidAmount_ShouldReturn400()
    {
        using var host = BuildController();
        var ctrl = (ChequesController)host.Controller;
        var req = new ChequeCreateRequest
        {
            Uuid = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            Player = "alex",
            Amount = 0,
            Note = "bad"
        };
        ctrl.TryValidateModel(req).Should().BeFalse();
        var res = await ctrl.Create(req) as ObjectResult;
        res.Should().NotBeNull();
        res!.Value.Should().BeOfType<ValidationProblemDetails>();
    }

    [Fact(DisplayName = "小切手使用: 並列実行でも先着のみ成功（FIFO直列化）")]
    public async Task Use_Concurrent_ShouldAllowOnlyFirst()
    {
        using var host = BuildController();
        var ctrl = (ChequesController)host.Controller;

        var create = new ChequeCreateRequest
        {
            Uuid = "dddddddd-dddd-dddd-dddd-dddddddddddd",
            Player = "alex",
            Amount = 1000,
            Note = "race"
        };
        // 残高を用意
        var db3 = host.Resources.OfType<TestDbFactory>().First();
        var bank3 = new BankService(db3.Factory);
        (await bank3.DepositAsync(new DepositRequest
        {
            Uuid = create.Uuid,
            Player = create.Player,
            Amount = create.Amount,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        })).StatusCode.Should().Be(200);
        var created = ((await ctrl.Create(create) as ObjectResult)!.Value as ApiResult<Cheque>)!.Data!;
        var id = created.Id;

        var players = Enumerable.Range(1, 10).Select(i => (uuid: $"00000000-0000-0000-0000-0000000000{i % 10}", player: $"p{i}" )).ToArray();
        var statuses = new ConcurrentBag<int?>();
        var tasks = players.Select(async p =>
        {
            var res = await ctrl.Use(id, new ChequeUseRequest { Uuid = p.uuid, Player = p.player }) as ObjectResult;
            statuses.Add(res!.StatusCode);
        }).ToArray();

        await Task.WhenAll(tasks);

        // 成功は1件、他は409
        statuses.Count(s => s == 200).Should().Be(1);
        statuses.Count(s => s == 409).Should().Be(players.Length - 1);

        var state = ((await ctrl.Get(id) as ObjectResult)!.Value as ApiResult<Cheque>)!.Data!;
        state.Used.Should().BeTrue();
        players.Select(p => p.player).Should().Contain(state.UsePlayer);
    }
}
