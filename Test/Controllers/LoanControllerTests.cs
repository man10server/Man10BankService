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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

public class LoanControllerTests
{
    private sealed record TestEnv(ControllerHost Host, BankService Bank, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static TestEnv BuildController()
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(LoanController).Assembly);

        var sp = services.BuildServiceProvider();
        var bank = new BankService(db.Factory);
        var loanService = new LoanService(db.Factory, bank);

        var ctrl = new LoanController(loanService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };

        var host = new ControllerHost
        {
            Controller = ctrl,
            Resources = [db, sp]
        };
        return new TestEnv(host, bank, db.Factory);
    }

    [Fact(DisplayName = "loan: 作成→借手UUID一覧→ID取得 を検証する")]
    public async Task Create_Then_GetByBorrower_And_GetById()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "11111111-1111-1111-1111-111111111111";
        const string borrowUuid = "22222222-2222-2222-2222-222222222222";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 5000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var createReq = new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 3000m,
            PaybackDate = DateTime.UtcNow.AddDays(7),
            CollateralItem = string.Empty
        };

        var createRes = await ctrl.Create(createReq);
        (createRes.Result as OkObjectResult).Should().NotBeNull();
        var created = (createRes.Value as Loan)!;

        var listRes = await ctrl.GetByBorrower(borrowUuid);
        (listRes.Result as OkObjectResult).Should().NotBeNull();
        var list = (listRes.Value as List<Loan>)!;
        list.Any(l => l.Id == created.Id).Should().BeTrue();

        var getRes = await ctrl.GetById(created.Id);
        (getRes.Result as OkObjectResult).Should().NotBeNull();
        var got = (getRes.Value as Loan)!;
        got.Id.Should().Be(created.Id);
        got.Amount.Should().Be(created.Amount);
        got.BorrowUuid.Should().Be(created.BorrowUuid);
    }

    [Fact(DisplayName = "loan: 担保なし 期限前は回収不可")]
    public async Task Repay_NoCollateral_BeforeDue_Partial()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "33333333-3333-3333-3333-333333333333";
        const string borrowUuid = "44444444-4444-4444-4444-444444444444";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 1000m,
            PaybackDate = DateTime.UtcNow.AddDays(5),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(400);
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(1000m);
    }

    [Fact(DisplayName = "loan: 担保なし 期限後は全額回収される（所持金十分）")]
    public async Task Repay_NoCollateral_AfterDue_Full()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "33333333-3333-3333-3333-333333333334";
        const string borrowUuid = "44444444-4444-4444-4444-444444444445";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 500m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(200);
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
    }

    [Fact(DisplayName = "loan: 担保なし 所持金なしは回収不可(400)")]
    public async Task Repay_NoCollateral_NoBalance_BadRequest()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "33333333-3333-3333-3333-333333333335";
        const string borrowUuid = "44444444-4444-4444-4444-444444444446";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 700m,
            PaybackDate = DateTime.UtcNow.AddDays(3),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "loan: 担保あり 所持金十分は全額回収(担保は残る)")]
    public async Task Repay_WithCollateral_BeforeDue_WithBalance_FullCollected()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "55555555-5555-5555-5555-555555555555";
        const string borrowUuid = "66666666-6666-6666-6666-666666666666";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 1200m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "diamond"
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(200);
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
        after.CollateralItem.Should().Be("diamond");
    }

    [Fact(DisplayName = "loan: 担保あり 返済完了後に担保リリースで空になる")]
    public async Task ReleaseCollateral_AfterFullRepay_EmptiesCollateral()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "55555555-5555-5555-5555-555555555556";
        const string borrowUuid = "66666666-6666-6666-6666-666666666667";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 1000m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "gold"
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(200);

        var release = await ctrl.ReleaseCollateral(loan.Id, borrowerUuid: borrowUuid) as ObjectResult;
        release!.StatusCode.Should().Be(200);
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.CollateralItem.Should().Be("");
    }

    [Fact(DisplayName = "loan: 担保あり 期限後・所持金なしは担保回収になる")]
    public async Task Repay_WithCollateral_AfterDue_NoBalance_CollateralCollected()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "55555555-5555-5555-5555-555555555557";
        const string borrowUuid = "66666666-6666-6666-6666-666666666668";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            Amount = 5000m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "emerald"
        }) as ObjectResult;
        var loan = (create!.Value as Loan)!;

        // 借手の残高を0に
        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }

        var lenderBefore = await env.Bank.GetBalanceAsync(lendUuid);
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(200);
        
        var lenderAfter = await env.Bank.GetBalanceAsync(lendUuid);
        lenderAfter.Data.Should().Be(lenderBefore.Data);

        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
        after.CollateralItem.Should().Be("");
    }

    private static async Task<Loan?> GetLoanAsync(IDbContextFactory<BankDbContext> f, int id)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }
}
