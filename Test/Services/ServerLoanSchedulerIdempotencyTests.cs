using FluentAssertions;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// スケジューラ冪等化(DESIGN 2.4)の検証: 日次利息の同日二重実行がスキップされること。
public class ServerLoanSchedulerIdempotencyTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    private static (ServerLoanService Loan, TestDbFactory Db) Build()
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
        return (loan, db);
    }

    [Fact(DisplayName = "日次利息: 同日二重実行は2回目がスキップされ残債が二重加算されない")]
    public async Task DailyInterest_RunTwiceSameDay_ShouldNotDoubleCharge()
    {
        var (loan, db) = Build();
        using var _ = db;

        // 残債を直接設定して利息対象を作る(借入上限の影響を受けない)
        var set = await loan.SetBorrowAmountAsync(Uuid, 100000m);
        set.IsSuccess.Should().BeTrue();

        var afterBorrow = (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount;
        afterBorrow.Should().Be(100000m);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1回目: 利息が加算される
        (await loan.HasDailyInterestRunAsync(today)).Should().BeFalse();
        await loan.RunDailyInterestForAllAsync(today);
        var afterFirst = (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount;
        afterFirst.Should().BeGreaterThan(afterBorrow);

        // 2回目: 当日分のInterestログが存在するためスキップされ、残債は変化しない
        (await loan.HasDailyInterestRunAsync(today)).Should().BeTrue();
        await loan.RunDailyInterestForAllAsync(today);
        var afterSecond = (await loan.GetByUuidAsync(Uuid)).Data!.BorrowAmount;
        afterSecond.Should().Be(afterFirst);

        // Interestログは1件のみ
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var interestCount = await ctx.ServerLoanLogs.AsNoTracking()
            .CountAsync(x => x.Uuid == Uuid && x.Action == "Interest");
        interestCount.Should().Be(1);
    }
}
