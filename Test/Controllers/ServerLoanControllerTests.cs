using System.Linq;
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

    private static TestEnv BuildController()
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
        var bank = new BankService(db.Factory);
        var loanService = new ServerLoanService(db.Factory, bank, config);

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
        const string uuid = "00000000-0000-0000-0000-000000000001";
        const string player = "steve";
        const decimal amount = 1000m;

        var res = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Player = player, Amount = amount }) as ObjectResult;
        res!.StatusCode.Should().Be(200);

        var loan = await GetLoanAsync(env.DbFactory, uuid);
        loan!.BorrowAmount.Should().Be(amount);
        var expectedPayment = Math.Round(amount * 0.01m * 7m * 2m, 0, MidpointRounding.AwayFromZero);
        loan.PaymentAmount.Should().Be(expectedPayment);

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.First().Should().BeEquivalentTo(new { Amount = amount, Deposit = true, Note = "loan_borrow" });

        var loanLogsRes = await ctrl.GetLogs(uuid, limit: 10) as ObjectResult;
        loanLogsRes!.StatusCode.Should().Be(200);
        var loanLogs = (loanLogsRes.Value as ApiResult<List<ServerLoanLog>>)!.Data!;
        loanLogs.Any(l => l is { Action: "Borrow", Amount: amount }).Should().BeTrue();
    }

    [Fact(DisplayName = "borrow: 借入可能額を超えると409で失敗する")]
    public async Task Borrow_OverLimit_ShouldFail409()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = "00000000-0000-0000-0000-000000000002";
        const string player = "alex";
        var limitRes = await ctrl.GetBorrowLimit(uuid) as ObjectResult;
        limitRes!.StatusCode.Should().Be(200);
        var limit = (limitRes.Value as ApiResult<decimal>)!.Data;
        var over = limit + 12345m;

        var res = await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Player = player, Amount = over }) as ObjectResult;
        res!.StatusCode.Should().Be(409);
    }

    [Fact(DisplayName = "repay: 出金・残債減少・ログ追記を検証する")]
    public async Task Repay_ShouldWithdraw_DecreaseRemain_AndWriteLogs()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = "00000000-0000-0000-0000-000000000003";
        const string player = "notch";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Player = player, Amount = 100000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Player = player, Amount = 1000m });
        var before = await GetLoanAsync(env.DbFactory, uuid);
        const decimal repayAmount = 6000m;
        var expectedRepayAmount = Math.Min(before!.BorrowAmount, repayAmount);
        var repayRes = await ctrl.Repay(uuid, amount: repayAmount) as ObjectResult;
        repayRes!.StatusCode.Should().Be(200);

        var after = await GetLoanAsync(env.DbFactory, uuid);
        after!.BorrowAmount.Should().Be(before.BorrowAmount - expectedRepayAmount);

        var moneyLogs = await GetMoneyLogsAsync(env.DbFactory, uuid);
        moneyLogs.First().Should().BeEquivalentTo(new { Amount = -expectedRepayAmount, Deposit = false, Note = "loan_repay" });

        var loanLogRes = await ctrl.GetLogs(uuid, limit: 10) as ObjectResult;
        loanLogRes!.StatusCode.Should().Be(200);
        var loanLogs = (loanLogRes.Value as ApiResult<List<ServerLoanLog>>)!.Data!;
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -expectedRepayAmount).Should().BeTrue();
    }

    [Fact(DisplayName = "repay: 金額未指定と過払い要求は残債にクリップして成功する")]
    public async Task Repay_NoAmount_And_Overpay_ShouldClipAndSucceed()
    {
        using var env = BuildController();
        var ctrl = (ServerLoanController)env.Host.Controller;
        const string uuid = "00000000-0000-0000-0000-000000000004";
        const string player = "jeb";
        await env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Player = player, Amount = 100000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

        await ctrl.Borrow(uuid, new ServerLoanBorrowBodyRequest { Player = player, Amount = 1000m });

        var before = await GetLoanAsync(env.DbFactory, uuid);
        var noArg = await ctrl.Repay(uuid, amount: null) as ObjectResult;
        noArg!.StatusCode.Should().Be(200);

        var after = await GetLoanAsync(env.DbFactory, uuid);
        var used1 = before!.BorrowAmount - after!.BorrowAmount;
        used1.Should().BeGreaterThan(0);

        var remain = after.BorrowAmount;
        var overRes = await ctrl.Repay(uuid, amount: remain + 9999m) as ObjectResult;
        overRes!.StatusCode.Should().Be(200);

        var final = await GetLoanAsync(env.DbFactory, uuid);
        final!.BorrowAmount.Should().Be(0);

        var loanLogRes = await ctrl.GetLogs(uuid, limit: 20) as ObjectResult;
        loanLogRes!.StatusCode.Should().Be(200);
        var loanLogs = (loanLogRes.Value as ApiResult<List<ServerLoanLog>>)!.Data!;
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -used1).Should().BeTrue();
        loanLogs.Any(l => l.Action == "RepaySuccess" && l.Amount == -remain).Should().BeTrue();
    }
    
    private static async Task<ServerLoan?> GetLoanAsync(IDbContextFactory<BankDbContext> f, string uuid)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.ServerLoans.AsNoTracking().FirstOrDefaultAsync(x => x.Uuid == uuid);
    }

    private static async Task<List<MoneyLog>> GetMoneyLogsAsync(IDbContextFactory<BankDbContext> f, string uuid)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.MoneyLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();
    }

}
