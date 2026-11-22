using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

[Collection(ServerLoanConfigCollection.Name)]
public class ServerLoanCreditEvaluationTests
{
    private const string Uuid = TestConstants.Uuid;
    private const decimal MinAmount = 1000m;

    private static (ServerLoanService Loan, BankService Bank, TestDbFactory Db) Build(
        Dictionary<string, string?>? overrides = null)
    {
        var db = TestDbFactory.Create();
        var settings = new Dictionary<string, string?>
        {
            ["ServerLoan:DailyInterestRate"] = "0.01",
            ["ServerLoan:MinAmount"] = "1000",
            ["ServerLoan:MaxAmount"] = "3000000"
        };
        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
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

    private static async Task AddRepayLogsAsync(TestDbFactory db, params (string Action, decimal Amount)[] logs)
    {
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var date = DateTime.UtcNow.AddDays(-logs.Length);
        foreach (var (action, amount) in logs)
        {
            ctx.ServerLoanLogs.Add(new ServerLoanLog
            {
                Uuid = Uuid,
                Player = "test-player",
                Action = action,
                Amount = amount,
                Date = date
            });
            date = date.AddDays(1);
        }
        await ctx.SaveChangesAsync();
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

    [Fact(DisplayName = "借入上限: 週次返済が成功すると返済額の50%だけ上がる")]
    public async Task BorrowLimit_WeeklyRepaySuccess_ShouldGrowByHalfOfPaidAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 300m)).IsSuccess.Should().BeTrue();

        await loan.RunWeeklyRepayForAllAsync();

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1150m);
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

    [Fact(DisplayName = "借入上限: 1回の増加は現在の枠の50%まで")]
    public async Task BorrowLimit_LargeRepaySuccess_ShouldCapGrowth()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await AddRepayLogsAsync(db, ("RepaySuccess", -5000m));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1500m);
    }

    [Fact(DisplayName = "借入上限: 増加係数と増加率の上限は設定で変えられる")]
    public async Task BorrowLimit_CustomGrowthSettings_ShouldApply()
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:GrowthFactor"] = "2",
            ["ServerLoan:MaxGrowthRate"] = "1"
        });
        using var _1 = db;

        // 1000 → 1600(300×2) → 3200(増加は枠の100%まで)
        await AddRepayLogsAsync(db, ("RepaySuccess", -300m), ("RepaySuccess", -5000m));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(3200m);
    }

    [Fact(DisplayName = "借入上限: 週次返済の失敗で枠が半減し端数は切り捨てる")]
    public async Task BorrowLimit_RepayFailure_ShouldHalveAndFloor()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        // 1000 → 1500 → 2250 → 3375 → 1687.5
        await AddRepayLogsAsync(db,
            ("RepaySuccess", -5000m),
            ("RepaySuccess", -5000m),
            ("RepaySuccess", -5000m),
            ("RepayFailure", 100m));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1687m);
    }

    [Fact(DisplayName = "借入上限: 失敗で下限額を下回らず、その後の成功でまた育つ")]
    public async Task BorrowLimit_SuccessAfterFailure_ShouldGrowFromMinAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        // 1000 → 1250 → 625(下限 1000) → 1100
        await AddRepayLogsAsync(db,
            ("RepaySuccess", -500m),
            ("RepayFailure", 100m),
            ("RepaySuccess", -200m));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1100m);
    }

    [Fact(DisplayName = "借入上限: 成功を重ねても上限額を超えない")]
    public async Task BorrowLimit_ManySuccesses_ShouldNotExceedMaxAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await AddRepayLogsAsync(db, Enumerable.Repeat(("RepaySuccess", -10000000m), 25).ToArray());

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(3000000m);
    }

    [Fact(DisplayName = "借入: 定期返済に失敗したままの間は新規借入できない")]
    public async Task Borrow_AfterWeeklyRepayFailure_ShouldFailWithRepaymentOverdue()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();

        var res = await loan.BorrowAsync(Uuid, 100m);

        res.IsSuccess.Should().BeFalse();
        res.Code.Should().Be(ErrorCode.RepaymentOverdue);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(500m);
    }

    [Fact(DisplayName = "借入: 定期返済の失敗後に手動返済しても借入は再開できない")]
    public async Task Borrow_AfterManualRepayFollowingFailure_ShouldStillFail()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();

        await bank.DepositAsync(new DepositRequest { Uuid = Uuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        (await loan.RepayAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();

        (await loan.BorrowAsync(Uuid, 100m)).Code.Should().Be(ErrorCode.RepaymentOverdue);
    }

    [Fact(DisplayName = "借入: 延滞後に手動で完済した場合は次の定期返済日に0円の成功として再開できる")]
    public async Task Borrow_AfterFullManualRepayAndNextWeeklyRun_ShouldSucceed()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();

        await bank.DepositAsync(new DepositRequest { Uuid = Uuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        (await loan.RepayAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        (await loan.BorrowAsync(Uuid, 100m)).Code.Should().Be(ErrorCode.RepaymentOverdue);

        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == 0m);
        (await loan.BorrowAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "週次返済: 延滞のない完済済みローンにはログを残さない")]
    public async Task WeeklyRepay_NoDebtWithoutOverdue_ShouldNotWriteLog()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        (await loan.RepayAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        await loan.RunWeeklyRepayForAllAsync();
        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().NotContain(l => l.Action == "RepaySuccess" || l.Action == "RepayFailure");
    }

    [Fact(DisplayName = "借入: 定期返済の失敗後に次の定期返済が成功すると借入を再開できる")]
    public async Task Borrow_AfterWeeklyRepaySuccessFollowingFailure_ShouldSucceed()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();

        await bank.DepositAsync(new DepositRequest { Uuid = Uuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });
        await loan.RunWeeklyRepayForAllAsync();

        (await loan.BorrowAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
    }
}
