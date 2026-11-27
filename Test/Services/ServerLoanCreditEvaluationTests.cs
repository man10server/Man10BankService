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
            ["ServerLoan:MaxAmount"] = "3000000",
            ["ServerLoan:GrowthFactor"] = "0.5",
            ["ServerLoan:MaxGrowthRate"] = "0.5",
            ["ServerLoan:MinPaymentRate"] = "0",
            ["ServerLoan:MinPaymentAmount"] = "0",
            ["ServerLoan:MinPrincipalRate"] = "0"
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

    // 定期返済の履歴を作る。成功は「返済額と同じ額を 1 週間借りてから全額返した」週として記録する。
    private static async Task AddRepayLogsAsync(TestDbFactory db, params (string Action, decimal Amount)[] logs)
    {
        var rows = new List<(string Action, decimal Amount, DateTime Date)>();
        var date = DateTime.UtcNow.AddDays(-8 * logs.Length - 1);
        foreach (var (action, amount) in logs)
        {
            if (action == "RepaySuccess")
            {
                rows.Add(("Borrow", -amount, date));
                for (var day = 1; day <= 7; day++)
                    rows.Add(("Interest", 0m, date.AddDays(day)));
                rows.Add((action, amount, date.AddDays(7).AddHours(1)));
            }
            else
            {
                rows.Add((action, amount, date));
            }
            date = date.AddDays(8);
        }
        await AddLogsAsync(db, rows.ToArray());
    }

    private static Task AddLogsAsync(TestDbFactory db, params (string Action, decimal Amount, double DaysAgo)[] logs)
    {
        var now = DateTime.UtcNow;
        return AddLogsAsync(db, logs.Select(l => (l.Action, l.Amount, now.AddDays(-l.DaysAgo))).ToArray());
    }

    private static async Task AddLogsAsync(TestDbFactory db, (string Action, decimal Amount, DateTime Date)[] logs)
    {
        await using var ctx = await db.Factory.CreateDbContextAsync();
        foreach (var (action, amount, date) in logs)
        {
            ctx.ServerLoanLogs.Add(new ServerLoanLog
            {
                Uuid = Uuid,
                Player = "test-player",
                Action = action,
                Amount = amount,
                Date = date
            });
        }
        await ctx.SaveChangesAsync();
    }

    // 日次利息を days 日分付ける(ログの日時は実行時刻になるため、すべて直近 1 週間以内の利息として扱われる)。
    private static async Task AccrueInterestAsync(ServerLoanService loan, int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var day = days; day >= 1; day--)
            await loan.RunDailyInterestForAllAsync(today.AddDays(-day));
    }

    private static Task DepositAsync(BankService bank, decimal amount) =>
        bank.DepositAsync(new DepositRequest
        {
            Uuid = Uuid,
            Amount = amount,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "seed",
            Server = "dev"
        });

    // 500 円借りて残高を空にし、週次返済を失敗させる(失敗額は 500 × 1% × 14 = 70)。
    private static async Task MakeOverdueAsync(ServerLoanService loan, BankService bank)
    {
        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        await WithdrawAllAsync(bank, 500m);
        await loan.RunWeeklyRepayForAllAsync();
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
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 返済履歴がなければ下限額になる")]
    public async Task BorrowLimit_NoHistory_ShouldBeMinAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 1週間借りてから週次返済が成功すると返済額の50%だけ上がる")]
    public async Task BorrowLimit_WeeklyRepaySuccess_ShouldGrowByHalfOfPaidAmount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 300m)).IsSuccess.Should().BeTrue();
        await AccrueInterestAsync(loan, 7);

        await loan.RunWeeklyRepayForAllAsync();

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1150m);
    }

    [Fact(DisplayName = "借入上限: 利息が付く前に返済した分では上がらない")]
    public async Task BorrowLimit_BorrowedJustBeforeRepay_ShouldNotGrow()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 300m)).IsSuccess.Should().BeTrue();

        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == -300m);
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 返済の前日に借りた分は7分の1だけ数える")]
    public async Task BorrowLimit_BorrowedOneDayBeforeRepay_ShouldGrowByOneSeventh()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, 700m)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 300m)).IsSuccess.Should().BeTrue();
        await AccrueInterestAsync(loan, 1);

        await loan.RunWeeklyRepayForAllAsync();

        // 平均残債 700 ÷ 7 = 100 が返済額 300 より小さいので、100 × 50% だけ増える。
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1050m);
    }

    [Fact(DisplayName = "借入上限: 少額を借りておき返済直前に借り増しても平均残債の分しか上がらない")]
    public async Task BorrowLimit_ToppedUpJustBeforeRepay_ShouldGrowByAverageDebt()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        // 利息が付いた時点の残債は 100, 101, ..., 106(平均 103)。
        await AddLogsAsync(db,
            ("Borrow", 100m, 7.5),
            ("Interest", 1m, 6.5), ("Interest", 1m, 5.5), ("Interest", 1m, 4.5), ("Interest", 1m, 3.5),
            ("Interest", 1m, 2.5), ("Interest", 1m, 1.5), ("Interest", 1m, 0.5),
            ("Borrow", 900m, 0.2),
            ("RepaySuccess", -1007m, 0.1));

        // 1000 + 103 × 50% = 1051.5
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1051m);
    }

    [Fact(DisplayName = "借入上限: 1週間より前に付いた利息は数えない")]
    public async Task BorrowLimit_InterestOlderThanAWeek_ShouldNotCount()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await AddLogsAsync(db,
            ("Borrow", 1000m, 20),
            ("Interest", 10m, 19), ("Interest", 10m, 18), ("Interest", 10m, 17), ("Interest", 10m, 16),
            ("Interest", 10m, 15), ("Interest", 10m, 14), ("Interest", 10m, 13),
            ("EarlyRepay", -1070m, 12),
            ("Borrow", 500m, 0.2),
            ("RepaySuccess", -500m, 0.1));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 定期返済の失敗より前に付いた利息は次の成功で数えない")]
    public async Task BorrowLimit_SuccessAfterFailureInSameWeek_ShouldNotGrow()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await AddLogsAsync(db,
            ("Borrow", 1000m, 7.5),
            ("Interest", 10m, 6.5), ("Interest", 10m, 5.5), ("Interest", 10m, 4.5), ("Interest", 10m, 3.5),
            ("Interest", 10m, 2.5), ("Interest", 10m, 1.5), ("Interest", 10m, 0.5),
            ("RepayFailure", 300m, 0.3),
            ("RepaySuccess", -1070m, 0.1));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
    }

    [Fact(DisplayName = "借入上限: 0円の返済成功(延滞解消の記録)はその週の利息の記録を消費しない")]
    public async Task BorrowLimit_ZeroAmountSuccessInWeek_ShouldKeepInterestHistory()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await AddLogsAsync(db,
            ("Borrow", 1000m, 7.5),
            ("Interest", 10m, 6.5), ("Interest", 10m, 5.5), ("Interest", 10m, 4.5),
            ("RepaySuccess", 0m, 4),
            ("Interest", 10m, 3.5), ("Interest", 10m, 2.5), ("Interest", 10m, 1.5), ("Interest", 10m, 0.5),
            ("RepaySuccess", -1070m, 0.1));

        // 平均残債 1030 に対して増加は枠の 50% が上限。
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1500m);
    }

    [Fact(DisplayName = "借入上限: ログに残っていない残債も現在の残債から逆算して平均残債に含める")]
    public async Task BorrowLimit_DebtWithoutBorrowLog_ShouldBeDerivedFromCurrentDebt()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.ServerLoans.Add(new ServerLoan
            {
                Uuid = Uuid,
                Player = "test-player",
                BorrowAmount = 4700m,
                PaymentAmount = 370m,
                LastPayDate = DateTime.UtcNow,
                StopInterest = false
            });
            await ctx.SaveChangesAsync();
        }

        // 借入ログは無いが、現在の残債 4700 から逆算すると利息が付いた時点の残債は 5000 〜 5060 になる。
        await AddLogsAsync(db,
            ("Interest", 10m, 6.5), ("Interest", 10m, 5.5), ("Interest", 10m, 4.5), ("Interest", 10m, 3.5),
            ("Interest", 10m, 2.5), ("Interest", 10m, 1.5), ("Interest", 10m, 0.5),
            ("RepaySuccess", -370m, 0.1));

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(1185m);
    }

    // 1 回の成功で枠は 500 増える(返済額 1000 × 0.5。上限は枠 × 0.5)。
    [Theory(DisplayName = "借入上限: 参加者がいる間だけ LinkedMaxAmount を上限にし、未設定なら MaxAmount のまま")]
    [InlineData("2000", "S", 1200)]
    [InlineData("2000", "S,J,S", 1700)]
    [InlineData("2000", "S,J,S,S", 2000)]
    [InlineData("2000", "S,J,S,S,L", 1200)]
    [InlineData("2000", "S,J,J,S,L,S", 2000)]
    [InlineData(null, "S,J,S,S", 1200)]
    public async Task BorrowLimit_LinkedMaxAmount_ShouldApplyWhileContractHasMembers(string? linkedMaxAmount, string history, int expected)
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:MaxAmount"] = "1200",
            ["ServerLoan:LinkedMaxAmount"] = linkedMaxAmount
        });
        using var _1 = db;

        await AddRepayLogsAsync(db, history.Split(',').Select(x => x switch
        {
            "S" => ("RepaySuccess", -1000m),
            "J" => ("MemberJoin", 0m),
            _ => ("MemberLeave", 0m)
        }).ToArray());

        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(expected);
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

    [Theory(DisplayName = "借入上限: 下限10万・上限300万・増加率10%では最短36回の定期返済で上限に届く")]
    [InlineData(35, false)]
    [InlineData(36, true)]
    public async Task BorrowLimit_TenPercentGrowth_ShouldReachMaxAfter36Successes(int successes, bool reachesMax)
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:MinAmount"] = "100000",
            ["ServerLoan:MaxGrowthRate"] = "0.1"
        });
        using var _1 = db;

        await AddRepayLogsAsync(db, Enumerable.Repeat(("RepaySuccess", -10000000m), successes).ToArray());

        var limit = (await loan.CalculateBorrowLimitAsync(Uuid)).Data;
        (limit == 3000000m).Should().Be(reachesMax);
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

    [Theory(DisplayName = "週次返済: 設定額と最低返済額(残債の割合・固定下限)の大きいほうを引き落とす")]
    [InlineData("0.1", "0", 10, 100)]
    [InlineData("0.1", "0", 500, 500)]
    [InlineData("0.1", "300", 10, 300)]
    [InlineData("0.1", "5000", 10, 1000)]
    [InlineData("0.1", "0", 0, 100)]
    public async Task WeeklyRepay_ShouldChargeAtLeastMinimumPayment(
        string minPaymentRate, string minPaymentAmount, int paymentAmount, int expectedPaid)
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:MinPaymentRate"] = minPaymentRate,
            ["ServerLoan:MinPaymentAmount"] = minPaymentAmount
        });
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, paymentAmount)).IsSuccess.Should().BeTrue();

        (await loan.GetPaymentInfoAsync(Uuid)).Data!.NextPaymentAmount.Should().Be(expectedPaid);

        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == -expectedPaid);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(MinAmount - expectedPaid);
    }

    // 残債 1000・日利 1% の 1 週間分の利息は 72(10, 10, 10, 10, 10, 11, 11)。
    [Theory(DisplayName = "週次返済: 最低返済額は1週間分の利息と元本分の合計を下回らない")]
    [InlineData("0.02", "0.01", "0", false, 82)]
    [InlineData("0.1", "0.01", "0", false, 100)]
    [InlineData("0.02", "0", "0", false, 20)]
    [InlineData("0.02", "0.01", "0", true, 20)]
    [InlineData("0.02", "0.01", "1000", false, 20)]
    public async Task WeeklyRepay_MinimumPayment_ShouldCoverWeeklyInterestAndPrincipal(
        string minPaymentRate, string minPrincipalRate, string maxDebtAmount, bool stopInterest, int expectedPaid)
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:MinPaymentRate"] = minPaymentRate,
            ["ServerLoan:MinPrincipalRate"] = minPrincipalRate,
            ["ServerLoan:MaxDebtAmount"] = maxDebtAmount
        });
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 1m)).IsSuccess.Should().BeTrue();
        if (stopInterest)
        {
            await using var ctx = await db.Factory.CreateDbContextAsync();
            (await ctx.ServerLoans.SingleAsync(x => x.Uuid == Uuid)).StopInterest = true;
            await ctx.SaveChangesAsync();
        }

        (await loan.GetPaymentInfoAsync(Uuid)).Data!.NextPaymentAmount.Should().Be(expectedPaid);

        await loan.RunWeeklyRepayForAllAsync();

        (await GetLogsAsync(db)).Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == -expectedPaid);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(MinAmount - expectedPaid);
    }

    [Fact(DisplayName = "ログ: プレイヤーの操作には操作者を、すべての操作に操作後の残債を記録する")]
    public async Task Logs_ShouldRecordActorAndDebtAfter()
    {
        var (loan, _, db) = Build();
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        await AccrueInterestAsync(loan, 1);
        (await loan.RepayAsync(Uuid, 10m)).IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
        await loan.RunWeeklyRepayForAllAsync();
        (await loan.SetBorrowAmountAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        var logs = (await GetLogsAsync(db)).OrderBy(l => l.Id).ToList();
        logs.Select(l => (l.Action, l.ActorUuid, l.DebtAfter)).Should().Equal(
            ("Borrow", Uuid, 1000m),
            ("Interest", null, 1010m),
            ("EarlyRepay", Uuid, 1000m),
            ("RepaySuccess", null, 900m),
            ("SetBorrowAmount", null, 500m));
    }

    [Fact(DisplayName = "手動返済: 最低返済額より少ない額でも返済できる")]
    public async Task ManualRepay_BelowMinimumPayment_ShouldSucceed()
    {
        var (loan, _, db) = Build(new Dictionary<string, string?>
        {
            ["ServerLoan:MinPaymentRate"] = "0.1",
            ["ServerLoan:MinPaymentAmount"] = "300"
        });
        using var _1 = db;

        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();
        (await loan.RepayAsync(Uuid, 10m)).IsSuccess.Should().BeTrue();

        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(MinAmount - 10m);
    }

    [Fact(DisplayName = "利息: 残債が上限額を超える分は付けない")]
    public async Task DailyInterest_ExceedingMaxDebtAmount_ShouldBeCapped()
    {
        var (loan, _, db) = Build(new Dictionary<string, string?> { ["ServerLoan:MaxDebtAmount"] = "1015" });
        using var _1 = db;
        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();

        // 1000 → 1010(利息 10) → 1015(利息 10 のうち上限までの 5) → 1015(利息なし)
        await AccrueInterestAsync(loan, 3);

        var logs = await GetLogsAsync(db);
        logs.Where(l => l.Action == "Interest").Select(l => l.Amount).Should().BeEquivalentTo([10m, 5m]);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(1015m);
        (await loan.GetPaymentInfoAsync(Uuid)).Data!.DailyInterestPerDay.Should().Be(0m);
    }

    [Fact(DisplayName = "利息: 残債がすでに上限額を超えている場合は付けず、残債も減らさない")]
    public async Task DailyInterest_DebtAboveMaxDebtAmount_ShouldNotChangeDebt()
    {
        var (loan, _, db) = Build(new Dictionary<string, string?> { ["ServerLoan:MaxDebtAmount"] = "1015" });
        using var _1 = db;
        (await loan.SetBorrowAmountAsync(Uuid, 5000m)).IsSuccess.Should().BeTrue();

        await AccrueInterestAsync(loan, 2);

        (await GetLogsAsync(db)).Should().NotContain(l => l.Action == "Interest");
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(5000m);
    }

    [Fact(DisplayName = "利息: 上限額が0以下なら上限なしで付ける")]
    public async Task DailyInterest_MaxDebtAmountZero_ShouldNotBeCapped()
    {
        var (loan, _, db) = Build(new Dictionary<string, string?> { ["ServerLoan:MaxDebtAmount"] = "0" });
        using var _1 = db;
        (await loan.BorrowAsync(Uuid, MinAmount)).IsSuccess.Should().BeTrue();

        await AccrueInterestAsync(loan, 2);

        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(1020m);
        (await loan.GetPaymentInfoAsync(Uuid)).Data!.DailyInterestPerDay.Should().Be(10m);
    }

    [Fact(DisplayName = "延滞回収: 入金があれば失敗額を回収して延滞を解消し、借入枠は増やさない")]
    public async Task OverdueCollection_WithBalance_ShouldCollectAndResolve()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;
        await MakeOverdueAsync(loan, bank);
        await DepositAsync(bank, 1000m);

        await loan.RunOverdueCollectionForAllAsync();

        var logs = await GetLogsAsync(db);
        logs.Should().ContainSingle(l => l.Action == "OverdueCollect" && l.Amount == -70m);
        logs.Should().ContainSingle(l => l.Action == "RepaySuccess" && l.Amount == 0m);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(430m);
        (await bank.GetBalanceAsync(Uuid)).Data.Should().Be(930m);
        (await loan.CalculateBorrowLimitAsync(Uuid)).Data.Should().Be(MinAmount);
        (await loan.BorrowAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞回収: 残高が足りなければある分だけ回収し、回収しきるまで延滞のまま")]
    public async Task OverdueCollection_PartialBalance_ShouldCollectAvailableAndStayOverdue()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;
        await MakeOverdueAsync(loan, bank);

        await DepositAsync(bank, 30m);
        await loan.RunOverdueCollectionForAllAsync();

        (await bank.GetBalanceAsync(Uuid)).Data.Should().Be(0m);
        (await loan.BorrowAsync(Uuid, 100m)).Code.Should().Be(ErrorCode.RepaymentOverdue);

        await DepositAsync(bank, 100m);
        await loan.RunOverdueCollectionForAllAsync();
        await loan.RunOverdueCollectionForAllAsync();

        var logs = await GetLogsAsync(db);
        logs.Where(l => l.Action == "OverdueCollect").Select(l => l.Amount).Should().BeEquivalentTo([-30m, -40m]);
        (await bank.GetBalanceAsync(Uuid)).Data.Should().Be(60m);
        (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount.Should().Be(430m);
        (await loan.BorrowAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞回収: 残高がなければ何も記録しない")]
    public async Task OverdueCollection_NoBalance_ShouldNotWriteLog()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;
        await MakeOverdueAsync(loan, bank);

        await loan.RunOverdueCollectionForAllAsync();

        (await GetLogsAsync(db)).Should().NotContain(l => l.Action == "OverdueCollect" || l.Action == "RepaySuccess");
    }

    [Fact(DisplayName = "延滞回収: 延滞していないローンからは回収しない")]
    public async Task OverdueCollection_NotOverdue_ShouldNotCollect()
    {
        var (loan, bank, db) = Build();
        using var _1 = db;
        (await loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        await loan.RunOverdueCollectionForAllAsync();

        (await GetLogsAsync(db)).Should().NotContain(l => l.Action == "OverdueCollect");
        (await bank.GetBalanceAsync(Uuid)).Data.Should().Be(500m);
    }
}
