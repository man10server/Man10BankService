using System.Linq;
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
        const string lendPlayer = "lender";
        const string borrowUuid = "22222222-2222-2222-2222-222222222222";
        const string borrowPlayer = "borrower";

        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Player = lendPlayer, Amount = 5000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var createReq = new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 3000m,
            PaybackDate = DateTime.UtcNow.AddDays(7),
            CollateralItem = string.Empty
        };

        var createRes = await ctrl.Create(createReq) as ObjectResult;
        createRes!.StatusCode.Should().Be(200);
        var created = (createRes.Value as ApiResult<Loan>)!.Data!;

        var listRes = await ctrl.GetByBorrower(borrowUuid) as ObjectResult;
        listRes!.StatusCode.Should().Be(200);
        var list = (listRes.Value as ApiResult<List<Loan>>)!.Data!;
        list.Any(l => l.Id == created.Id).Should().BeTrue();

        var getRes = await ctrl.GetById(created.Id) as ObjectResult;
        getRes!.StatusCode.Should().Be(200);
        var got = (getRes.Value as ApiResult<Loan>)!.Data!;
        got.Id.Should().Be(created.Id);
    }

    [Fact(DisplayName = "loan: 担保なし 期限前に一部回収される")]
    public async Task Repay_NoCollateral_BeforeDue_Partial()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "33333333-3333-3333-3333-333333333333";
        const string lendPlayer = "lender2";
        const string borrowUuid = "44444444-4444-4444-4444-444444444444";
        const string borrowPlayer = "borrower2";

        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Player = lendPlayer, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 1000m,
            PaybackDate = DateTime.UtcNow.AddDays(5),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as ApiResult<Loan>)!.Data!;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Player = borrowPlayer, Amount = 600m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(200);
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(400m);
    }

    [Fact(DisplayName = "loan: 担保なし 期限後は全額回収される（所持金十分）")]
    public async Task Repay_NoCollateral_AfterDue_Full()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "33333333-3333-3333-3333-333333333334";
        const string lendPlayer = "lender2";
        const string borrowUuid = "44444444-4444-4444-4444-444444444445";
        const string borrowPlayer = "borrower2";

        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Player = lendPlayer, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 500m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as ApiResult<Loan>)!.Data!;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Player = borrowPlayer, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
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
        const string lendPlayer = "lender2";
        const string borrowUuid = "44444444-4444-4444-4444-444444444446";
        const string borrowPlayer = "borrower2";

        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Player = lendPlayer, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 700m,
            PaybackDate = DateTime.UtcNow.AddDays(3),
            CollateralItem = string.Empty
        }) as ObjectResult;
        var loan = (create!.Value as ApiResult<Loan>)!.Data!;

        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Player = borrowPlayer, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid) as ObjectResult;
        repay!.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "loan: 担保あり 返済（期限前・期限後・所持金有無）と担保リリース")]
    public async Task Repay_WithCollateral_And_Release()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = "55555555-5555-5555-5555-555555555555";
        const string lendPlayer = "lender3";
        const string borrowUuid = "66666666-6666-6666-6666-666666666666";
        const string borrowPlayer = "borrower3";

        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Player = lendPlayer, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        // 期限前 充分な所持金 → 全額回収、担保は残る
        var create1 = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 1200m,
            PaybackDate = DateTime.UtcNow.AddDays(5),
            CollateralItem = "diamond"
        }) as ObjectResult;
        var loan1 = (create1!.Value as ApiResult<Loan>)!.Data!;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Player = borrowPlayer, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var repay1 = await ctrl.Repay(loan1.Id, collectorUuid: lendUuid) as ObjectResult;
        repay1!.StatusCode.Should().Be(200);
        var after1 = await GetLoanAsync(env.DbFactory, loan1.Id);
        after1!.Amount.Should().Be(0m);
        after1.CollateralItem.Should().Be("diamond");

        // 担保のリリース（返却）
        var release = await ctrl.ReleaseCollateral(loan1.Id, borrowerUuid: borrowUuid) as ObjectResult;
        release!.StatusCode.Should().Be(200);
        var afterRelease = await GetLoanAsync(env.DbFactory, loan1.Id);
        afterRelease!.CollateralItem.Should().Be("");

        // 期限後 所持金不足 → 担保回収（Amount=0, Collateral=Empty, collectorに入金なし）
        var create2 = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            LendPlayer = lendPlayer,
            BorrowUuid = borrowUuid,
            BorrowPlayer = borrowPlayer,
            Amount = 5000m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "emerald"
        }) as ObjectResult;
        var loan2 = (create2!.Value as ApiResult<Loan>)!.Data!;

        // 借手の残高を0に
        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Player = borrowPlayer, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }

        var lenderBefore = await env.Bank.GetBalanceAsync(lendUuid);
        var repay2 = await ctrl.Repay(loan2.Id, collectorUuid: lendUuid) as ObjectResult;
        repay2!.StatusCode.Should().Be(200);
        var lenderAfter = await env.Bank.GetBalanceAsync(lendUuid);
        lenderAfter.Data.Should().Be(lenderBefore.Data); // 担保回収時は入金されない

        var after2 = await GetLoanAsync(env.DbFactory, loan2.Id);
        after2!.Amount.Should().Be(0m);
        after2.CollateralItem.Should().Be("");
    }

    private static async Task<Loan?> GetLoanAsync(IDbContextFactory<BankDbContext> f, int id)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }
}
