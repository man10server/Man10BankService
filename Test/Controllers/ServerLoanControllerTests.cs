using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
using Man10BankService.Models;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

public class ServerLoanControllerTests
{
    private sealed record TestEnv(ControllerHost Host, BankService Bank, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static TestEnv BuildController(Action<FakePlayerProfileService>? configureProfile = null)
    {
        var db = TestDbFactory.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(ServerLoanController).Assembly);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServerLoan:DailyInterestRate"] = "0.01",
                ["ServerLoan:MinAmount"] = "1000",
                ["ServerLoan:MaxAmount"] = "3000000",
                ["ServerLoan:RepayWindow"] = "10"
            })
            .Build();

        var sp = services.BuildServiceProvider();
        var profile = new FakePlayerProfileService();
        configureProfile?.Invoke(profile);
        var bank = new BankService(db.Factory, profile);
        var loanService = new ServerLoanService(db.Factory, bank, profile, config);

        var ctrl = new ServerLoanController(loanService)
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
    
    [Fact(DisplayName = "borrow: 借入・初期支払額設定・入金・ログを検証する")]
    public async Task Borrow_ShouldUpdateLoan_SetPayment_Deposit_AndWriteLogs()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        const decimal amount = 1000m;

        var res = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = amount });
        res.Result.Should().BeOfType<OkObjectResult>();

        var loanRes = await ctrl.GetByUuid(uuid);
        var loan = loanRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        loan!.BorrowAmount.Should().Be(amount);
        var expectedPayment = Math.Round(amount * 0.01m * 7m * 2m, 0, MidpointRounding.AwayFromZero);
        loan.PaymentAmount.Should().Be(expectedPayment);

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.First().Should().BeEquivalentTo(new { Amount = amount, Deposit = true, Note = "loan_borrow" });

        var loanLogsRes = await ctrl.GetLogs(uuid, limit: 10);
        var loanLogs = loanLogsRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;
        loanLogs.Any(l => l is { Action: "Borrow", Amount: amount }).Should().BeTrue();
    }

    [Fact(DisplayName = "borrow: 借入可能額を超えると409で失敗する")]
    public async Task Borrow_OverLimit_ShouldFail409()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        var limitRes = await ctrl.GetBorrowLimit(uuid);
        var limit = (decimal)limitRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value!;
        var res = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = limit * 2 });
        res.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact(DisplayName = "borrow: 2回に分けて借入し、合計が反映されログも2件記録される")]
    public async Task Borrow_Twice_ShouldAccumulate_AndLogTwice()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        var b1 = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 400m });
        b1.Result.Should().BeOfType<OkObjectResult>();
        var b2 = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 500m });
        b2.Result.Should().BeOfType<OkObjectResult>();

        var loanRes = await ctrl.GetByUuid(uuid);
        var loan = loanRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        loan!.BorrowAmount.Should().Be(900m);

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.Count(l => l.Note == "loan_borrow").Should().Be(2);
        moneyLogs.Where(l => l.Note == "loan_borrow").Sum(l => l.Amount).Should().Be(900m);

        var loanLogsRes = await ctrl.GetLogs(uuid, limit: 10);
        var loanLogs = loanLogsRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;
        loanLogs.Count(l => l.Action == "Borrow").Should().Be(2);
        loanLogs.Where(l => l.Action == "Borrow").Sum(l => l.Amount).Should().Be(900m);
    }

    [Fact(DisplayName = "borrow: 1回目=Limit/2, 2回目=Limit×2 は2回目で409になる")]
    public async Task Borrow_Twice_FirstHalf_SecondDouble_ShouldConflict()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;

        var limitRes = await ctrl.GetBorrowLimit(uuid);
        var limit = (decimal)limitRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value!;

        var first = limit / 2m;
        var second = limit * 2m;

        var firstRes = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = first });
        firstRes.Result.Should().BeOfType<OkObjectResult>();

        var afterFirstRes = await ctrl.GetByUuid(uuid);
        var afterFirst = afterFirstRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        afterFirst!.BorrowAmount.Should().Be(first);

        var secondRes = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = second });
        secondRes.Result.Should().BeOfType<ConflictObjectResult>();

        var afterSecondRes = await ctrl.GetByUuid(uuid);
        var afterSecond = afterSecondRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        afterSecond!.BorrowAmount.Should().Be(first);
    }

    [Fact(DisplayName = "borrow-amount: 残額を上書きし支払額を再計算する")]
    public async Task SetBorrowAmount_ShouldOverwriteBorrowAmount_AndRecalculatePayment()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;

        var borrowRes = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 1000m });
        borrowRes.Result.Should().BeOfType<OkObjectResult>();

        var setRes = await ctrl.SetBorrowAmount(uuid, 5000m);
        setRes.Result.Should().BeOfType<OkObjectResult>();

        var loanRes = await ctrl.GetByUuid(uuid);
        var loan = loanRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        loan!.BorrowAmount.Should().Be(5000m);
        loan.PaymentAmount.Should().Be(Math.Round(5000m * 0.01m * 7m * 2m, 0, MidpointRounding.AwayFromZero));
    }

    [Fact(DisplayName = "borrow-amount: 0指定で残額と支払額を0にする")]
    public async Task SetBorrowAmount_Zero_ShouldSetBorrowAndPaymentToZero()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;

        var borrowRes = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 800m });
        borrowRes.Result.Should().BeOfType<OkObjectResult>();

        var setRes = await ctrl.SetBorrowAmount(uuid, 0m);
        setRes.Result.Should().BeOfType<OkObjectResult>();

        var loanRes = await ctrl.GetByUuid(uuid);
        var loan = loanRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        loan!.BorrowAmount.Should().Be(0m);
        loan.PaymentAmount.Should().Be(0m);
    }

    [Fact(DisplayName = "borrow-amount: 負数指定は400になる")]
    public async Task SetBorrowAmount_Negative_ShouldReturn400()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;

        var res = await ctrl.SetBorrowAmount(uuid, -1m);
        var bad = res.Result.Should().BeOfType<BadRequestObjectResult>().Which;
        var pd = bad.Value.Should().BeOfType<ProblemDetails>().Which;
        pd.Extensions["code"].Should().Be(ErrorCode.BorrowAmountMustBeZeroOrGreater.ToString());
    }

    [Fact(DisplayName = "borrow-amount: UUID不正時は400になる")]
    public async Task SetBorrowAmount_InvalidUuid_ShouldReturn400()
    {
        using var env = BuildController(profile => profile.SetName("invalid-uuid", null));
        var ctrl = (ServerLoanController)env.Host.Controller;

        var res = await ctrl.SetBorrowAmount("invalid-uuid", 1000m);
        var bad = res.Result.Should().BeOfType<BadRequestObjectResult>().Which;
        var pd = bad.Value.Should().BeOfType<ProblemDetails>().Which;
        pd.Extensions["code"].Should().Be(ErrorCode.PlayerNotFound.ToString());
    }

    [Fact(DisplayName = "borrow-amount: ローン未作成のUUIDでも新規作成して設定できる")]
    public async Task SetBorrowAmount_WithoutHistory_ShouldCreateAndSet()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;

        var res = await ctrl.SetBorrowAmount(TestConstants.BorrowUuid, 1000m);
        var ok = res.Result.Should().BeOfType<OkObjectResult>().Which;
        var loan = ok.Value.Should().BeOfType<ServerLoan>().Which;
        loan.BorrowAmount.Should().Be(1000m);
        loan.PaymentAmount.Should().Be(Math.Round(1000m * 0.01m * 7m * 2m, 0, MidpointRounding.AwayFromZero));
    }

    [Fact(DisplayName = "borrow-amount: 差分ログがSetBorrowAmountで記録される")]
    public async Task SetBorrowAmount_ShouldWriteDeltaLog()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;

        var borrowRes = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 1000m });
        borrowRes.Result.Should().BeOfType<OkObjectResult>();

        var setRes = await ctrl.SetBorrowAmount(uuid, 1300m);
        setRes.Result.Should().BeOfType<OkObjectResult>();

        var logsRes = await ctrl.GetLogs(uuid, limit: 10);
        var logs = logsRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;

        logs.Any(x => x.Action == "SetBorrowAmount" && x.Amount == 300m).Should().BeTrue();
    }

    [Fact(DisplayName = "repay: 所持金不足で409・借入ログは変化せず失敗ログが記録される")]
    public async Task Repay_InsufficientBalance_Should409_And_LogFailure()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        var borrowOk = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 500m });
        borrowOk.Result.Should().BeOfType<OkObjectResult>();

        await env.Bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = uuid,
            Amount = 500m,
            PluginName = "test",
            Note = "spend_all",
            DisplayNote = "支出",
            Server = "dev"
        });

        var res2 = await ctrl.Repay(uuid, amount: 100m);
        res2.Result.Should().BeOfType<ConflictObjectResult>();

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.First().Note.Should().NotBe("loan_repay");

        var loanLogRes = await ctrl.GetLogs(uuid, limit: 10);
        var loanLogs = loanLogRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;
        loanLogs.Any(l => l.Action == "RepayFailure").Should().BeTrue();
    }

    [Fact(DisplayName = "repay: 出金・残債減少・ログ追記を検証する")]
    public async Task Repay_ShouldWithdraw_DecreaseRemain_AndWriteLogs()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Amount = 100000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 1000m });
        
        var beforeRes = await ctrl.GetByUuid(uuid);
        var before = beforeRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;

        const decimal repayAmount = 6000m;
        var expectedRepayAmount = Math.Min(before!.BorrowAmount, repayAmount);
        var repayRes = await ctrl.Repay(uuid, amount: repayAmount);
        repayRes.Result.Should().BeOfType<OkObjectResult>();

        var afterRes = await ctrl.GetByUuid(uuid);
        var after = afterRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        after!.BorrowAmount.Should().Be(before.BorrowAmount - expectedRepayAmount);

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.First().Should().BeEquivalentTo(new { Amount = -expectedRepayAmount, Deposit = false, Note = "loan_repay" });

        var loanLogRes = await ctrl.GetLogs(uuid, limit: 10);
        var loanLogs = loanLogRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -expectedRepayAmount).Should().BeTrue();
    }

    [Fact(DisplayName = "repay: 金額未指定と過払い要求は残債にクリップして成功する")]
    public async Task Repay_NoAmount_And_Overpay_ShouldClipAndSucceed()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = TestConstants.Uuid;
        await env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Amount = 100000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Amount = 1000m });

        var beforeRes = await ctrl.GetByUuid(uuid);
        var before = beforeRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        var noArg = await ctrl.Repay(uuid, amount: null);
        noArg.Result.Should().BeOfType<OkObjectResult>();

        var afterRes = await ctrl.GetByUuid(uuid);
        var after = afterRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        var used1 = before!.BorrowAmount - after!.BorrowAmount;
        used1.Should().BeGreaterThan(0);

        var remain = after.BorrowAmount;
        var overRes = await ctrl.Repay(uuid, amount: remain + 9999m);
        overRes.Result.Should().BeOfType<OkObjectResult>();

        var finalRes = await ctrl.GetByUuid(uuid);
        var final = finalRes.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ServerLoan>().Which;
        final!.BorrowAmount.Should().Be(0);

        var loanLogRes2 = await ctrl.GetLogs(uuid, limit: 20);
        var loanLogs = loanLogRes2.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<List<ServerLoanLog>>().Which;
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -used1).Should().BeTrue();
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -remain).Should().BeTrue();
    }

    private static async Task<List<MoneyLog>> GetMoneyLogsAsync(IDbContextFactory<BankDbContext> f, string uuid)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.MoneyLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();
    }
}
