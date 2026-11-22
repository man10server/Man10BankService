using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

public class ServerLoanCreditEvaluationTests
{
    private const string Uuid = TestConstants.Uuid;
    private const decimal MinAmount = 1000m;

    private static (ServerLoanService Loan, BankService Bank, TestDbFactory Db) Build()
    {
        var db = TestDbFactory.Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServerLoan:DailyInterestRate"] = "0.01",
                ["ServerLoan:MinAmount"] = "1000",
                ["ServerLoan:MaxAmount"] = "3000000",
                ["ServerLoan:RepayWindow"] = "10"
            })
            .Build();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);
        return (loan, bank, db);
    }

    private static async Task<List<ServerLoanLog>> GetLogsAsync(TestDbFactory db)
    {
        await using var ctx = await db.Factory.CreateDbContextAsync();
        return await ctx.ServerLoanLogs.AsNoTracking().Where(x => x.Uuid == Uuid).ToListAsync();
    }

    private static Task WithdrawAllAsync(BankService bank, decimal amount) =>
        bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = Uuid,
            Amount = amount,
            PluginName = "test",
            Note = "spend_all",
            DisplayNote = "支出",
            Server = "dev"
        });

    [Fact(DisplayName = "週次返済: 成功は RepaySuccess として記録される")]
    public async Task WeeklyRepay_Success_ShouldLogRepaySuccess()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        var payment = (await loan.GetByUuidAsync(Uuid)).Data!.PaymentAmount;

        await loan.RunWeeklyRepayForAllAsync();

        var logs = await GetLogsAsync(db);
        logs.Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == -payment);
        logs.Should().NotContain(l => l.Action == "EarlyRepay");
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(MinAmount - payment);
    }

    [Fact(DisplayName = "週次返済: 残高不足は RepayFailure として記録され失敗回数が増える")]
    public async Task WeeklyRepay_InsufficientBalance_ShouldLogRepayFailure()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, MinAmount);

        await loan.RunWeeklyRepayForAllAsync();

        var logs = await GetLogsAsync(db);
        logs.Should().ContainSingle(l => l.Action == "RepayFailure");
        var after = (await loan.GetByUuidAsync(Uuid)).Data!;
        after.FailedPayment.Should().Be(1);
        after.BorrowAmount.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 返済履歴がなければ下限額になる")]
    public async Task BorrowLimit_NoHistory_ShouldBeMinAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 週次返済の成功額の平均×5まで上がる")]
    public async Task BorrowLimit_WeeklyRepaySuccess_ShouldRaiseLimit()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        await loan.RunWeeklyRepayForAllAsync();

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(2500m);
    }

    [Fact(DisplayName = "借入上限: 手動返済は上限に影響しない")]
    public async Task BorrowLimit_ManualRepay_ShouldNotRaiseLimit()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.RepayAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 下限額を超える返済に失敗しても上限は上がらない")]
    public async Task BorrowLimit_LargeRepayFailure_ShouldNotRaiseLimit()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.SetBorrowAmountAsync(Uuid, 5000m)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 5000m)).IsSuccess.Should().BeTrue();

        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().ContainSingle(l => l.Action == "RepayFailure" && l.Amount == 5000m);
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 週次返済の失敗は成功実績で上がった上限を引き下げる")]
    public async Task BorrowLimit_RepayFailureAfterSuccess_ShouldLowerLimit()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await loan.RunWeeklyRepayForAllAsync();
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(2500m);

        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }
}
