using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
using Man10BankService.Models.Database;
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
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var loanService = new LoanService(db.Factory, bank, profile);

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

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 5000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var createReq = new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 1000m,
            RepayAmount = 3000m,
            PaybackDate = DateTime.UtcNow.AddDays(7),
            CollateralItem = string.Empty
        };

        var createRes = await ctrl.Create(createReq);
        var created = createRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        var listRes = await ctrl.GetByBorrower(borrowUuid);
        var list = listRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<Loan>>().Which;
        list.Any(l => l.Id == created.Id).Should().BeTrue();

        var getRes = await ctrl.GetById(created.Id);
        var got = getRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;
        got.Id.Should().Be(created.Id);
        got.Amount.Should().Be(created.Amount);
        got.BorrowUuid.Should().Be(created.BorrowUuid);
    }

    [Fact(DisplayName = "loan: 担保なし 期限前は回収不可")]
    public async Task Repay_NoCollateral_BeforeDue_Partial()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 1000m,
            RepayAmount = 1000m,
            PaybackDate = DateTime.UtcNow.AddDays(5),
            CollateralItem = string.Empty
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        repay.Result.Should().BeOfType<ConflictObjectResult>();
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(1000m);
    }

    [Fact(DisplayName = "loan: 担保なし 期限後は全額回収される（所持金十分）")]
    public async Task Repay_NoCollateral_AfterDue_Full()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 100m,
            RepayAmount = 500m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = string.Empty
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        var okRes = repay.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<LoanRepayResponse>().Which;
        okRes.LoanId.Should().Be(loan.Id);
        okRes.Outcome.Should().Be(LoanRepayOutcome.Paid);
        okRes.CollectedAmount.Should().Be(loan.Amount);
        okRes.RemainingAmount.Should().Be(0m);
        okRes.CollateralItem.Should().BeNull();
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
    }

    [Fact(DisplayName = "loan: 担保なし 所持金なしは回収不可")]
    public async Task Repay_NoCollateral_NoBalance_BadRequest()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 10000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 100m,
            RepayAmount = 700m,
            PaybackDate = DateTime.UtcNow.AddDays(3),
            CollateralItem = string.Empty
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        repay.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "loan: 担保あり 所持金十分は全額回収(担保は残る)")]
    public async Task Repay_WithCollateral_BeforeDue_WithBalance_FullCollected()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 100m,
            RepayAmount = 1200m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "diamond"
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        var okRes = repay.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<LoanRepayResponse>().Which;
        okRes.LoanId.Should().Be(loan.Id);
        okRes.Outcome.Should().Be(LoanRepayOutcome.Paid);
        okRes.CollectedAmount.Should().Be(loan.Amount);
        okRes.RemainingAmount.Should().Be(0m);
        okRes.CollateralItem.Should().BeNull();
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
        after.CollateralItem.Should().Be("diamond");
    }

    [Fact(DisplayName = "loan: 担保あり 返済完了後に担保リリースで空になる")]
    public async Task ReleaseCollateral_AfterFullRepay_EmptiesCollateral()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 100m,
            RepayAmount = 1000m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "gold"
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        await env.Bank.DepositAsync(new DepositRequest { Uuid = borrowUuid, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        repay.Result.Should().BeOfType<OkObjectResult>();

        var release = await ctrl.ReleaseCollateral(loan.Id, borrowerUuid: borrowUuid);
        release.Result.Should().BeOfType<OkObjectResult>();
        var releaseAgain = await ctrl.ReleaseCollateral(loan.Id, borrowerUuid: borrowUuid);
        var releaseAgainConflict = releaseAgain.Result.Should().BeOfType<ConflictObjectResult>().Which;
        var releaseAgainPd = releaseAgainConflict.Value.Should().BeOfType<ProblemDetails>().Which;
        releaseAgainPd.Title.Should().Be(ErrorCode.CollateralAlreadyReleased.ToString());
        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.CollateralItem.Should().Be("gold");
        after.CollateralReleased.Should().BeTrue();
    }

    [Fact(DisplayName = "loan: 担保あり 期限後・所持金なしは担保回収になる")]
    public async Task Repay_WithCollateral_AfterDue_NoBalance_CollateralCollected()
    {
        using var env = BuildController();
        var ctrl = (LoanController)env.Host.Controller;

        const string lendUuid = TestConstants.LendUuid;
        const string borrowUuid = TestConstants.BorrowUuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = lendUuid, Amount = 20000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        var create = await ctrl.Create(new LoanCreateRequest
        {
            LendUuid = lendUuid,
            BorrowUuid = borrowUuid,
            BorrowAmount = 100m,
            RepayAmount = 5000m,
            PaybackDate = DateTime.UtcNow.AddDays(-1),
            CollateralItem = "emerald"
        });
        var loan = create.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<Loan>().Which;

        // 借手の残高を0に
        var bal = await env.Bank.GetBalanceAsync(borrowUuid);
        if (bal.Data > 0)
        {
            await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = borrowUuid, Amount = bal.Data, PluginName = "test", Note = "zero", DisplayNote = "zero", Server = "dev" });
        }

        var lenderBefore = await env.Bank.GetBalanceAsync(lendUuid);
        var repay = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        var okRes = repay.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<LoanRepayResponse>().Which;
        okRes.LoanId.Should().Be(loan.Id);
        okRes.Outcome.Should().Be(LoanRepayOutcome.CollateralCollected);
        okRes.CollectedAmount.Should().Be(0m);
        okRes.RemainingAmount.Should().Be(0m);
        okRes.CollateralItem.Should().Be("emerald");

        var repayAgain = await ctrl.Repay(loan.Id, collectorUuid: lendUuid);
        var repayAgainConflict = repayAgain.Result.Should().BeOfType<ConflictObjectResult>().Which;
        var repayAgainPd = repayAgainConflict.Value.Should().BeOfType<ProblemDetails>().Which;
        repayAgainPd.Title.Should().Be(ErrorCode.CollateralAlreadyReleased.ToString());
        
        var lenderAfter = await env.Bank.GetBalanceAsync(lendUuid);
        lenderAfter.Data.Should().Be(lenderBefore.Data);

        var after = await GetLoanAsync(env.DbFactory, loan.Id);
        after!.Amount.Should().Be(0m);
        after.CollateralItem.Should().Be("emerald");
        after.CollateralReleased.Should().BeTrue();
    }

    private static async Task<Loan?> GetLoanAsync(IDbContextFactory<BankDbContext> f, int id)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }
}
