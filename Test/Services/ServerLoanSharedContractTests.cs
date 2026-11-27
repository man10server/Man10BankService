using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 他のプレイヤーの契約に参加しているアカウント(メンバー)が、本垢の契約を共有することの検証。
[Collection(ServerLoanConfigCollection.Name)]
public class ServerLoanSharedContractTests
{
    private const string Owner = TestConstants.Uuid;
    private const string Member = "11111111-1111-1111-1111-111111111111";
    private const decimal MinAmount = 1000m;

    private sealed record Env(ServerLoanService Loan, BankService Bank, TestDbFactory Db) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static Env Build(Dictionary<string, string?>? overrides = null)
    {
        var db = TestDbFactory.Create();
        var settings = new Dictionary<string, string?>
        {
            ["ServerLoan:DailyInterestRate"] = "0.01",
            ["ServerLoan:MinAmount"] = "1000",
            ["ServerLoan:MaxAmount"] = "3000000",
            ["ServerLoan:MinPaymentRate"] = "0",
            ["ServerLoan:MinPaymentAmount"] = "0",
            ["ServerLoan:MinPrincipalRate"] = "0"
        };
        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var profile = new FakePlayerProfileService();
        profile.SetName(Owner, "owner");
        profile.SetName(Member, "member");
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);
        return new Env(loan, bank, db);
    }

    // メンバー表に直接行を入れて、Member を Owner の契約に参加させる(approved = false なら承認待ち)。
    private static async Task AddMemberRowAsync(Env env, bool approved = true)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        ctx.ServerLoanMembers.Add(new ServerLoanMember
        {
            Uuid = Member,
            OwnerUuid = Owner,
            LinkedBy = "member",
            ApprovedAt = approved ? DateTime.UtcNow : null
        });
        await ctx.SaveChangesAsync();
    }

    private static Task<ApiResult<decimal>> DepositAsync(Env env, string uuid, decimal amount) =>
        env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

    private static Task<ApiResult<decimal>> WithdrawAsync(Env env, string uuid, decimal amount) =>
        env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = uuid, Amount = amount, PluginName = "test", Note = "spend", DisplayNote = "支出", Server = "dev" });

    private static async Task<decimal> BalanceAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.UserBanks.AsNoTracking().Where(x => x.Uuid == uuid).Select(x => x.Balance).FirstOrDefaultAsync();
    }

    private static async Task<List<ServerLoanLog>> LogsAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.ServerLoanLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderBy(x => x.Id).ToListAsync();
    }

    [Fact(DisplayName = "共有: メンバーには本垢の契約・借入枠・ログが見える")]
    public async Task Member_ShouldSeeOwnersContract()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        await AddMemberRowAsync(env);

        var loan = (await env.Loan.GetByUuidAsync(Member)).Data!;
        loan.Uuid.Should().Be(Owner);
        loan.Player.Should().Be("owner");
        loan.BorrowAmount.Should().Be(500m);
        (await env.Loan.CalculateBorrowLimitAsync(Member)).Data.Should().Be(MinAmount);
        (await env.Loan.GetLogsAsync(Member)).Data!.Should().ContainSingle(l => l.Action == "Borrow" && l.Uuid == Owner);
        (await env.Loan.GetPaymentInfoAsync(Member)).Data!.DailyInterestPerDay.Should().Be(5m);
    }

    [Fact(DisplayName = "共有: 承認待ちのアカウントは自分の契約を使う")]
    public async Task PendingMember_ShouldUseOwnContract()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        await AddMemberRowAsync(env, approved: false);

        var loan = (await env.Loan.GetByUuidAsync(Member)).Data!;
        loan.Uuid.Should().Be(Member);
        loan.BorrowAmount.Should().Be(0m);
    }

    [Fact(DisplayName = "共有: メンバーが借りると本垢の契約の残債が増え、お金はメンバーの口座に入る")]
    public async Task Member_Borrow_ShouldAddDebtToOwnersContractAndDepositToMember()
    {
        using var env = Build();
        await AddMemberRowAsync(env);

        var res = await env.Loan.BorrowAsync(Member, 300m);

        res.IsSuccess.Should().BeTrue();
        res.Data!.Uuid.Should().Be(Owner);
        res.Data.Player.Should().Be("owner");
        res.Data.BorrowAmount.Should().Be(300m);
        (await BalanceAsync(env, Member)).Should().Be(300m);
        (await BalanceAsync(env, Owner)).Should().Be(0m);
        (await LogsAsync(env, Owner)).Select(l => (l.Action, l.Player, l.ActorUuid, l.DebtAfter))
            .Should().Equal(("Borrow", "owner", Member, 300m));
        (await LogsAsync(env, Member)).Should().BeEmpty();
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        (await ctx.ServerLoans.AsNoTracking().Select(x => x.Uuid).ToListAsync()).Should().Equal(Owner);
    }

    [Fact(DisplayName = "共有: 借入枠は契約で1つで、本垢とメンバーの借入の合計で判定する")]
    public async Task Member_Borrow_ShouldShareLimit()
    {
        using var env = Build();
        await AddMemberRowAsync(env);
        (await env.Loan.BorrowAsync(Owner, 800m)).IsSuccess.Should().BeTrue();

        (await env.Loan.BorrowAsync(Member, 300m)).Code.Should().Be(ErrorCode.BorrowLimitExceeded);
        (await env.Loan.BorrowAsync(Member, 200m)).IsSuccess.Should().BeTrue();

        (await env.Loan.GetByUuidAsync(Owner)).Data!.BorrowAmount.Should().Be(1000m);
    }

    [Fact(DisplayName = "共有: メンバーの手動返済はメンバーの口座から引き落とし、本垢の契約の残債を減らす")]
    public async Task Member_ManualRepay_ShouldWithdrawFromMemberAndReduceOwnersDebt()
    {
        using var env = Build();
        await AddMemberRowAsync(env);
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await DepositAsync(env, Member, 200m)).IsSuccess.Should().BeTrue();

        (await env.Loan.RepayAsync(Member, 200m)).IsSuccess.Should().BeTrue();

        (await env.Loan.GetByUuidAsync(Owner)).Data!.BorrowAmount.Should().Be(300m);
        (await BalanceAsync(env, Member)).Should().Be(0m);
        (await BalanceAsync(env, Owner)).Should().Be(500m);
        (await LogsAsync(env, Owner)).Last().Should().Match<ServerLoanLog>(l =>
            l.Action == "EarlyRepay" && l.Amount == -200m && l.ActorUuid == Member && l.DebtAfter == 300m);
    }

    [Fact(DisplayName = "共有: 本垢の契約が延滞すると、メンバーも借入・出金ができない")]
    public async Task Member_ShouldShareOverdue()
    {
        using var env = Build();
        await AddMemberRowAsync(env);
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await WithdrawAsync(env, Owner, 500m)).IsSuccess.Should().BeTrue();
        (await DepositAsync(env, Member, 1000m)).IsSuccess.Should().BeTrue();
        // 契約の定期返済が失敗した状態を作る。
        await using (var ctx = await env.Db.Factory.CreateDbContextAsync())
        {
            ctx.ServerLoanLogs.Add(new ServerLoanLog { Uuid = Owner, Player = "owner", Action = "RepayFailure", Amount = 70m });
            await ctx.SaveChangesAsync();
        }

        (await env.Loan.HasOverdueDebtAsync(Member)).Data.Should().BeTrue();
        (await env.Loan.BorrowAsync(Member, 100m)).Code.Should().Be(ErrorCode.RepaymentOverdue);
        (await WithdrawAsync(env, Member, 100m)).Code.Should().Be(ErrorCode.WithdrawBlockedByOverdue);
        (await BalanceAsync(env, Member)).Should().Be(1000m);
    }

    [Fact(DisplayName = "共有: メンバーが支払額を変えると本垢の契約の支払額が変わる")]
    public async Task Member_SetPaymentAmount_ShouldUpdateOwnersContract()
    {
        using var env = Build();
        await AddMemberRowAsync(env);
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();

        (await env.Loan.SetPaymentAmountAsync(Member, 123m)).IsSuccess.Should().BeTrue();

        (await env.Loan.GetByUuidAsync(Owner)).Data!.PaymentAmount.Should().Be(123m);
    }
}
