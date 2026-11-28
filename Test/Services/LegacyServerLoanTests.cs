using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 旧リボ(0.5.0 までの借入の残債)の検証。
// 利息なし・借入不可で、残っている間は新リボを借りられず、完済すると新リボに切り替わる。
[Collection(ServerLoanConfigCollection.Name)]
public class LegacyServerLoanTests
{
    private const string Player = TestConstants.Uuid;
    private const string Other = "11111111-1111-1111-1111-111111111111";
    private const decimal MinAmount = 1000m;

    private sealed record Env(
        ServerLoanService Loan, LegacyServerLoanService Legacy, ServerLoanController Controller,
        BankService Bank, EstateService Estate, TestDbFactory Db) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static Env Build()
    {
        var db = TestDbFactory.Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServerLoan:DailyInterestRate"] = "0.01",
                ["ServerLoan:MinAmount"] = "1000",
                ["ServerLoan:MaxAmount"] = "3000000",
                ["ServerLoan:MinPaymentRate"] = "0",
                ["ServerLoan:MinPaymentAmount"] = "0",
                ["ServerLoan:MinPrincipalRate"] = "0"
            })
            .Build();
        var profile = new FakePlayerProfileService();
        profile.SetName(Player, "player");
        profile.SetName(Other, "other");
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);
        var legacy = new LegacyServerLoanService(db.Factory, bank, profile);
        var controller = new ServerLoanController(loan, legacy)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return new Env(loan, legacy, controller, bank, new EstateService(db.Factory, profile), db);
    }

    // 旧リボの行(server_loan_tbl)を直接入れる。
    private static async Task SeedLegacyAsync(Env env, string uuid, decimal debt, decimal payment)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        ctx.LegacyServerLoans.Add(new LegacyServerLoan
        {
            Uuid = uuid,
            Player = uuid == Player ? "player" : "other",
            BorrowAmount = debt,
            PaymentAmount = payment,
            LastPayDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task AddMemberRowAsync(Env env, string member, string owner)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        ctx.ServerLoanMembers.Add(new ServerLoanMember { Uuid = member, OwnerUuid = owner, LinkedBy = "test", ApprovedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
    }

    private static Task<ApiResult<decimal>> DepositAsync(Env env, string uuid, decimal amount) =>
        env.Bank.DepositAsync(new DepositRequest { Uuid = uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

    private static async Task<decimal> BalanceAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.UserBanks.AsNoTracking().Where(x => x.Uuid == uuid).Select(x => x.Balance).FirstOrDefaultAsync();
    }

    private static async Task<decimal> LegacyDebtAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.LegacyServerLoans.AsNoTracking().Where(x => x.Uuid == uuid).Select(x => x.BorrowAmount).SingleAsync();
    }

    private static async Task<List<(string Action, decimal Amount)>> LegacyLogsAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        var logs = await ctx.LegacyServerLoanLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderBy(x => x.Id).ToListAsync();
        return logs.Select(x => (x.Action, x.Amount)).ToList();
    }

    private static T OkValue<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<T>().Subject;

    private static string ErrorCodeOf<T>(ActionResult<T> result) =>
        (string)result.Result.Should().BeAssignableTo<ObjectResult>().Subject.Value.Should().BeOfType<ProblemDetails>().Subject.Extensions["code"]!;

    // ===== 借入の停止 =====

    [Fact(DisplayName = "旧リボ: 残債が残っている間は新リボを借りられない")]
    public async Task Borrow_ShouldFail_WhileLegacyDebtRemains()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        var res = await env.Loan.BorrowAsync(Player, 100m);

        res.Code.Should().Be(ErrorCode.LegacyLoanOutstanding);
        (await env.Loan.GetByUuidAsync(Player)).Data!.BorrowAmount.Should().Be(0m);
        (await BalanceAsync(env, Player)).Should().Be(0m);
    }

    [Fact(DisplayName = "旧リボ: 完済すると新リボを借りられる")]
    public async Task Borrow_ShouldSucceed_AfterLegacyDebtRepaid()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        (await DepositAsync(env, Player, 500m)).IsSuccess.Should().BeTrue();

        (await env.Legacy.RepayAsync(Player, 500m)).IsSuccess.Should().BeTrue();

        (await env.Legacy.HasDebtAsync(Player)).Data.Should().BeFalse();
        (await env.Loan.BorrowAsync(Player, 100m)).IsSuccess.Should().BeTrue();
        (await env.Loan.GetByUuidAsync(Player)).Data!.BorrowAmount.Should().Be(100m);
    }

    [Fact(DisplayName = "旧リボ: 同じリボの本垢に旧リボが残っていると、参加者も借りられない")]
    public async Task Borrow_ShouldFail_ForMember_WhenOwnerHasLegacyDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        await AddMemberRowAsync(env, member: Other, owner: Player);

        (await env.Loan.BorrowAsync(Other, 100m)).Code.Should().Be(ErrorCode.LegacyLoanOutstanding);
    }

    [Fact(DisplayName = "旧リボ: 同じリボの参加者に旧リボが残っていると、本垢も借りられない")]
    public async Task Borrow_ShouldFail_ForOwner_WhenMemberHasLegacyDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Other, debt: 500m, payment: 100m);
        await AddMemberRowAsync(env, member: Other, owner: Player);

        (await env.Loan.BorrowAsync(Player, 100m)).Code.Should().Be(ErrorCode.LegacyLoanOutstanding);
    }

    [Fact(DisplayName = "旧リボ: ほかのプレイヤーの旧リボは借入に影響しない")]
    public async Task Borrow_ShouldSucceed_WhenOnlyUnrelatedPlayerHasLegacyDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Other, debt: 500m, payment: 100m);

        (await env.Loan.BorrowAsync(Player, 100m)).IsSuccess.Should().BeTrue();
    }

    // ===== 利息 =====

    [Fact(DisplayName = "旧リボ: 日次の利息は付かない")]
    public async Task DailyInterest_ShouldNotApplyToLegacyDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        await env.Loan.RunDailyInterestForAllAsync();
        await env.Loan.RunOverdueCollectionForAllAsync();

        (await LegacyDebtAsync(env, Player)).Should().Be(500m);
        (await LegacyLogsAsync(env, Player)).Should().BeEmpty();
    }

    // ===== 返済 =====

    [Fact(DisplayName = "旧リボ: 手動返済で口座から引き落とし、残債を減らしてログを残す(過払いは残債まで)")]
    public async Task Repay_ShouldWithdrawAndReduceDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        (await DepositAsync(env, Player, 1000m)).IsSuccess.Should().BeTrue();

        (await env.Legacy.RepayAsync(Player, 200m)).Data!.BorrowAmount.Should().Be(300m);
        (await env.Legacy.RepayAsync(Player, 9999m)).Data!.BorrowAmount.Should().Be(0m);

        (await BalanceAsync(env, Player)).Should().Be(500m);
        (await LegacyLogsAsync(env, Player)).Should().Equal(("EarlyRepay", -200m), ("EarlyRepay", -300m));
        (await env.Legacy.RepayAsync(Player, 1m)).Code.Should().Be(ErrorCode.NoRepaymentNeeded);
    }

    [Fact(DisplayName = "旧リボ: 手動返済は残高不足なら失敗し、何も記録しない")]
    public async Task Repay_InsufficientFunds_ShouldFailWithoutLog()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        (await DepositAsync(env, Player, 50m)).IsSuccess.Should().BeTrue();

        (await env.Legacy.RepayAsync(Player, 200m)).Code.Should().Be(ErrorCode.InsufficientFunds);

        (await LegacyDebtAsync(env, Player)).Should().Be(500m);
        (await BalanceAsync(env, Player)).Should().Be(50m);
        (await LegacyLogsAsync(env, Player)).Should().BeEmpty();
    }

    [Fact(DisplayName = "旧リボ: 定期返済は支払額(設定)どおりに引き落とす(最低返済額は無い)")]
    public async Task WeeklyRepay_ShouldWithdrawPaymentAmount()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 1m);
        (await DepositAsync(env, Player, 1000m)).IsSuccess.Should().BeTrue();

        await env.Legacy.RunWeeklyRepayForAllAsync();

        (await LegacyDebtAsync(env, Player)).Should().Be(499m);
        (await BalanceAsync(env, Player)).Should().Be(999m);
        (await LegacyLogsAsync(env, Player)).Should().Equal(("RepaySuccess", -1m));
    }

    [Fact(DisplayName = "旧リボ: 定期返済は残債を超えて引き落とさない")]
    public async Task WeeklyRepay_ShouldNotExceedDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 30m, payment: 100m);
        (await DepositAsync(env, Player, 1000m)).IsSuccess.Should().BeTrue();

        await env.Legacy.RunWeeklyRepayForAllAsync();

        (await LegacyDebtAsync(env, Player)).Should().Be(0m);
        (await BalanceAsync(env, Player)).Should().Be(970m);
    }

    [Fact(DisplayName = "旧リボ: 定期返済の残高不足は失敗を記録するだけで、延滞の制限は掛からない")]
    public async Task WeeklyRepay_InsufficientFunds_ShouldLogFailureWithoutOverdue()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        (await DepositAsync(env, Player, 50m)).IsSuccess.Should().BeTrue();

        await env.Legacy.RunWeeklyRepayForAllAsync();

        (await LegacyDebtAsync(env, Player)).Should().Be(500m);
        (await BalanceAsync(env, Player)).Should().Be(50m);
        (await LegacyLogsAsync(env, Player)).Should().Equal(("RepayFailure", 100m));
        (await env.Loan.HasOverdueDebtAsync(Player)).Data.Should().BeFalse();
        (await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = Player, Amount = 50m, PluginName = "test", Note = "spend", DisplayNote = "支出", Server = "dev" }))
            .IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "旧リボ: 支払額(設定)が 0 なら定期返済では引き落とさない")]
    public async Task WeeklyRepay_PaymentNotSet_ShouldDoNothing()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 0m);
        (await DepositAsync(env, Player, 1000m)).IsSuccess.Should().BeTrue();

        await env.Legacy.RunWeeklyRepayForAllAsync();

        (await LegacyDebtAsync(env, Player)).Should().Be(500m);
        (await LegacyLogsAsync(env, Player)).Should().BeEmpty();
    }

    [Fact(DisplayName = "旧リボ: 完済済み(残債 0)の行は定期返済の対象にならない")]
    public async Task WeeklyRepay_ShouldSkipPaidOffRows()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 0m, payment: 100m);
        (await DepositAsync(env, Player, 1000m)).IsSuccess.Should().BeTrue();

        await env.Legacy.RunWeeklyRepayForAllAsync();

        (await BalanceAsync(env, Player)).Should().Be(1000m);
        (await env.Legacy.HasDebtAsync(Player)).Data.Should().BeFalse();
    }

    // ===== API の振り分け =====

    [Fact(DisplayName = "API: 旧リボが残っているプレイヤーには旧リボを legacy=true で返し、完済後は新リボを返す")]
    public async Task Get_ShouldReturnLegacyUntilRepaid()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);
        (await DepositAsync(env, Player, 500m)).IsSuccess.Should().BeTrue();

        var before = OkValue(await env.Controller.GetByUuid(Player));
        before.Should().Match<ServerLoanResponse>(x => x.Legacy && x.BorrowAmount == 500m && x.PaymentAmount == 100m && x.StopInterest);

        var info = OkValue(await env.Controller.GetPaymentInfo(Player));
        info.DailyInterestPerDay.Should().Be(0m);
        info.NextPaymentAmount.Should().Be(100m);

        var repaid = OkValue(await env.Controller.Repay(Player, 500m));
        repaid.Should().Match<ServerLoanResponse>(x => x.Legacy && x.BorrowAmount == 0m);

        var after = OkValue(await env.Controller.GetByUuid(Player));
        after.Should().Match<ServerLoanResponse>(x => !x.Legacy && x.BorrowAmount == 0m);
    }

    [Fact(DisplayName = "API: 旧リボが残っている間、借入は LegacyLoanOutstanding(409)になる")]
    public async Task Borrow_ShouldReturn409_WhileLegacyDebtRemains()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        var res = await env.Controller.Borrow(Player, new ServerLoanBorrowBodyRequest { Amount = 100m });

        res.Result.Should().BeOfType<ConflictObjectResult>();
        ErrorCodeOf(res).Should().Be("LegacyLoanOutstanding");
    }

    [Fact(DisplayName = "API: 旧リボが残っている間、支払額の変更・残債の上書き・ログは旧リボに対して行う")]
    public async Task PaymentAmount_BorrowAmount_Logs_ShouldTargetLegacy()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        OkValue(await env.Controller.SetPaymentAmount(Player, 250m)).Should().Match<ServerLoanResponse>(x => x.Legacy && x.PaymentAmount == 250m);
        OkValue(await env.Controller.SetBorrowAmount(Player, 400m)).Should().Match<ServerLoanResponse>(x => x.Legacy && x.BorrowAmount == 400m && x.PaymentAmount == 250m);

        var logs = OkValue(await env.Controller.GetLogs(Player));
        logs.Should().ContainSingle().Which.Should().Match<ServerLoanLogResponse>(x =>
            x.Action == "SetBorrowAmount" && x.Amount == -100m && x.ActorUuid == null && x.DebtAfter == null);
        (await env.Loan.GetLogsAsync(Player)).Data!.Should().BeEmpty();
    }

    [Fact(DisplayName = "API: 運営が旧リボの残債を 0 に上書きすると、新リボに切り替わる")]
    public async Task SetBorrowAmountZero_ShouldSwitchToCurrentLoan()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        OkValue(await env.Controller.SetBorrowAmount(Player, 0m)).Should().Match<ServerLoanResponse>(x => x.Legacy && x.BorrowAmount == 0m);

        OkValue(await env.Controller.GetByUuid(Player)).Legacy.Should().BeFalse();
        OkValue(await env.Controller.Borrow(Player, new ServerLoanBorrowBodyRequest { Amount = 100m })).BorrowAmount.Should().Be(100m);
    }

    [Fact(DisplayName = "API: 旧リボが無いプレイヤーの操作は新リボに対して行う")]
    public async Task NoLegacy_ShouldUseCurrentLoan()
    {
        using var env = Build();

        OkValue(await env.Controller.Borrow(Player, new ServerLoanBorrowBodyRequest { Amount = 300m })).Should().Match<ServerLoanResponse>(x => !x.Legacy && x.BorrowAmount == 300m);
        OkValue(await env.Controller.Repay(Player, 100m)).Should().Match<ServerLoanResponse>(x => !x.Legacy && x.BorrowAmount == 200m);
        OkValue(await env.Controller.GetLogs(Player)).Select(x => x.Action).Should().Equal("EarlyRepay", "Borrow");
    }

    // ===== 資産 =====

    [Fact(DisplayName = "資産: 旧リボの残債も負債として数える")]
    public async Task Estate_ShouldIncludeLegacyDebt()
    {
        using var env = Build();
        await SeedLegacyAsync(env, Player, debt: 500m, payment: 100m);

        (await env.Estate.UpdateSnapshotAsync(Player, new EstateUpdateRequest())).IsSuccess.Should().BeTrue();

        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        var estate = await ctx.Estates.AsNoTracking().SingleAsync(x => x.Uuid == Player);
        estate.Loan.Should().Be(500m);
        estate.Total.Should().Be(-500m);
    }
}
