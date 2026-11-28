using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 元本の記録と、利息の上限(未払いの利息が 元本 × MaxInterestMultiplier を超えない)の検証。
[Collection(ServerLoanConfigCollection.Name)]
public class ServerLoanInterestCapTests
{
    private const string Owner = TestConstants.Uuid;
    private const string Member = "11111111-1111-1111-1111-111111111111";

    private sealed record Env(ServerLoanService Loan, BankService Bank, TestDbFactory Db) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    // 日利 10%、初期枠 5000。multiplier を省略すると利息の上限なし(既定値)。
    private static Env Build(string? multiplier = null, string minPrincipalRate = "0")
    {
        var db = TestDbFactory.Create();
        var settings = new Dictionary<string, string?>
        {
            ["ServerLoan:DailyInterestRate"] = "0.1",
            ["ServerLoan:MinAmount"] = "5000",
            ["ServerLoan:MaxAmount"] = "3000000",
            ["ServerLoan:MinPaymentRate"] = "0",
            ["ServerLoan:MinPaymentAmount"] = "0",
            ["ServerLoan:MinPrincipalRate"] = minPrincipalRate
        };
        if (multiplier != null)
            settings["ServerLoan:MaxInterestMultiplier"] = multiplier;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var profile = new FakePlayerProfileService();
        profile.SetName(Owner, "owner");
        profile.SetName(Member, "member");
        var bank = new BankService(db.Factory, profile);
        return new Env(new ServerLoanService(db.Factory, bank, profile, config), bank, db);
    }

    // days 日分の利息を付ける(利息は日付ごとに 1 回なので、過去の別々の日付の分として実行する)。
    private static async Task AccrueAsync(Env env, int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var day = 1; day <= days; day++)
            await env.Loan.RunDailyInterestForAllAsync(today.AddDays(-day));
    }

    private static async Task<(decimal Debt, decimal Principal)> StateAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        var loan = await ctx.ServerLoans.AsNoTracking().SingleAsync(x => x.Uuid == uuid);
        return (loan.BorrowAmount, loan.Principal);
    }

    private static async Task<List<ServerLoanLog>> LogsAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.ServerLoanLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderBy(x => x.Id).ToListAsync();
    }

    // ===== 元本の記録 =====

    [Fact(DisplayName = "元本: 借入で増え、利息では増えず、返済は利息から先に充てる")]
    public async Task Principal_ShouldTrackBorrowInterestAndRepay()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1000m, 1000m));

        await AccrueAsync(env, 1);
        (await StateAsync(env, Owner)).Should().Be((1100m, 1000m));

        // 未払いの利息 100 のうち 60 を返す。元本は減らない。
        (await env.Loan.RepayAsync(Owner, 60m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1040m, 1000m));

        // 残りの利息 40 を返し、超えた 60 が元本に充てられる。
        (await env.Loan.RepayAsync(Owner, 100m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((940m, 940m));

        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1440m, 1440m));

        (await LogsAsync(env, Owner)).Select(l => (l.Action, l.DebtAfter, l.PrincipalAfter)).Should().Equal(
            ("Borrow", 1000m, 1000m),
            ("Interest", 1100m, 1000m),
            ("EarlyRepay", 1040m, 1000m),
            ("EarlyRepay", 940m, 940m),
            ("Borrow", 1440m, 1440m));
    }

    [Fact(DisplayName = "元本: 定期返済も利息から先に充てる")]
    public async Task Principal_ScheduledRepay_ShouldPayInterestFirst()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 1);
        (await env.Loan.SetPaymentAmountAsync(Owner, 250m)).IsSuccess.Should().BeTrue();

        await env.Loan.RunWeeklyRepayForAllAsync();

        (await StateAsync(env, Owner)).Should().Be((850m, 850m));
    }

    [Fact(DisplayName = "元本: 運営が残債を引き上げた分は元本になり、引き下げは利息から先に減らす")]
    public async Task Principal_SetBorrowAmount_ShouldFollowDebt()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 1);

        (await env.Loan.SetBorrowAmountAsync(Owner, 1500m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1500m, 1400m));

        (await env.Loan.SetBorrowAmountAsync(Owner, 1450m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1450m, 1400m));

        (await env.Loan.SetBorrowAmountAsync(Owner, 300m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((300m, 300m));

        (await env.Loan.SetBorrowAmountAsync(Owner, 0m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((0m, 0m));
    }

    [Fact(DisplayName = "元本: 参加したアカウントの残債は、元本と利息の内訳ごと本垢の契約に移る")]
    public async Task Principal_MemberJoin_ShouldCarryBreakdown()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        (await env.Loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 1);
        (await StateAsync(env, Member)).Should().Be((330m, 300m));

        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();

        (await StateAsync(env, Owner)).Should().Be((1430m, 1300m));
        (await StateAsync(env, Member)).Should().Be((0m, 0m));
        (await LogsAsync(env, Owner)).Last().Should().Match<ServerLoanLog>(l =>
            l.Action == "MemberJoin" && l.Amount == 330m && l.DebtAfter == 1430m && l.PrincipalAfter == 1300m);
    }

    // ===== 利息の上限 =====

    [Fact(DisplayName = "利息の上限: 未払いの利息は 元本 × 倍率 で止まる")]
    public async Task InterestCap_ShouldStopAtPrincipalTimesMultiplier()
    {
        using var env = Build(multiplier: "0.25");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();

        // 1 日目 +100、2 日目 +110、3 日目は +121 のところ上限(250)までの +40、4 日目以降は付かない。
        await AccrueAsync(env, 5);

        (await StateAsync(env, Owner)).Should().Be((1250m, 1000m));
        (await LogsAsync(env, Owner)).Where(l => l.Action == "Interest").Select(l => l.Amount).Should().Equal(100m, 110m, 40m);
        (await env.Loan.GetPaymentInfoAsync(Owner)).Data!.DailyInterestPerDay.Should().Be(0m);
    }

    [Fact(DisplayName = "利息の上限: 利息を返すと、その分だけまた利息が付く")]
    public async Task InterestCap_ShouldAccrueAgainAfterRepay()
    {
        using var env = Build(multiplier: "0.25");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 3);
        (await env.Loan.RepayAsync(Owner, 100m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((1150m, 1000m));

        // 残債 1150 × 10% = 115 のところ、上限までの残り 100 だけ付く。
        (await env.Loan.GetPaymentInfoAsync(Owner)).Data!.DailyInterestPerDay.Should().Be(100m);
        await AccrueAsync(env, 1);

        (await StateAsync(env, Owner)).Should().Be((1250m, 1000m));
    }

    [Fact(DisplayName = "利息の上限: 追加で借りると、元本が増えた分だけ上限も上がる")]
    public async Task InterestCap_ShouldRiseWithPrincipal()
    {
        using var env = Build(multiplier: "0.25");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 3);

        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((2250m, 2000m));

        // 上限は 2000 × 0.25 = 500。未払いの利息 250 から、225 が付いて 475 になる。
        await AccrueAsync(env, 1);
        (await StateAsync(env, Owner)).Should().Be((2475m, 2000m));
    }

    [Fact(DisplayName = "利息の上限: 元本を返すと上限も下がり、超えている間は利息が付かない")]
    public async Task InterestCap_ShouldFallWithPrincipal()
    {
        using var env = Build(multiplier: "0.25");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await AccrueAsync(env, 1);

        // 利息 100 を返し切り、元本を 400 返す。元本 600 の上限は 150。
        (await env.Loan.RepayAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await StateAsync(env, Owner)).Should().Be((600m, 600m));

        await AccrueAsync(env, 5);
        (await StateAsync(env, Owner)).Should().Be((750m, 600m));
    }

    [Fact(DisplayName = "利息の上限: 倍率が未設定(0)なら上限なし")]
    public async Task InterestCap_Disabled_ShouldNotLimit()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();

        await AccrueAsync(env, 5);

        // 1000 → 1100 → 1210 → 1331 → 1464 → 1610(1 円未満は四捨五入)
        (await StateAsync(env, Owner)).Should().Be((1610m, 1000m));
    }

    [Fact(DisplayName = "利息の上限: 残債の上限(MaxDebtAmount)と両方ある場合は、先に達したほうで止まる")]
    public async Task InterestCap_ShouldCombineWithMaxDebtAmount()
    {
        using var env = Build(multiplier: "5");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        await using (var ctx = await env.Db.Factory.CreateDbContextAsync())
        {
            // 残債の上限(既定 1000 万円)の手前まで、利息だけが積み上がった状態を作る。
            var loan = await ctx.ServerLoans.SingleAsync(x => x.Uuid == Owner);
            loan.BorrowAmount = 5900m;
            await ctx.SaveChangesAsync();
        }

        // 利息の上限は 1000 × 5 = 5000。未払いの利息 4900 から、100 だけ付く。
        await AccrueAsync(env, 2);

        (await StateAsync(env, Owner)).Should().Be((6000m, 1000m));
    }

    [Fact(DisplayName = "利息の上限: 最低返済額に含める 1 週間分の利息も、上限までしか数えない")]
    public async Task InterestCap_MinimumPayment_ShouldUseCappedInterest()
    {
        using var env = Build(multiplier: "0.25", minPrincipalRate: "0.01");
        (await env.Loan.BorrowAsync(Owner, 1000m)).IsSuccess.Should().BeTrue();
        (await env.Loan.SetPaymentAmountAsync(Owner, 1m)).IsSuccess.Should().BeTrue();

        // 上限に達する前: 1 週間分の利息は上限までの 250、元本分は 1000 × 1% = 10。
        (await env.Loan.GetPaymentInfoAsync(Owner)).Data!.NextPaymentAmount.Should().Be(260m);

        // 上限に達した後: これ以上利息は付かないので、元本分(1250 × 1% の切り上げ)だけになる。
        await AccrueAsync(env, 5);
        (await StateAsync(env, Owner)).Should().Be((1250m, 1000m));
        (await env.Loan.GetPaymentInfoAsync(Owner)).Data!.NextPaymentAmount.Should().Be(13m);
    }
}
