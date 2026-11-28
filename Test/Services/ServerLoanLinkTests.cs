using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 契約の共有(リンク)の申請・承認・解除と、運営による強制リンクの検証。
[Collection(ServerLoanConfigCollection.Name)]
public class ServerLoanLinkTests
{
    private const string Owner = TestConstants.Uuid;
    private const string Member = "11111111-1111-1111-1111-111111111111";
    private const string Other = "22222222-2222-2222-2222-222222222222";
    private const string Unknown = "33333333-3333-3333-3333-333333333333";

    private sealed record Env(ServerLoanService Loan, BankService Bank, TestDbFactory Db) : IDisposable
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
        profile.SetName(Owner, "owner");
        profile.SetName(Member, "member");
        profile.SetName(Other, "other");
        profile.SetName(Unknown, null);
        var bank = new BankService(db.Factory, profile);
        return new Env(new ServerLoanService(db.Factory, bank, profile, config), bank, db);
    }

    private static async Task<List<ServerLoanLog>> LogsAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.ServerLoanLogs.AsNoTracking().Where(x => x.Uuid == uuid).OrderBy(x => x.Id).ToListAsync();
    }

    private static async Task<decimal> DebtAsync(Env env, string uuid)
    {
        await using var ctx = await env.Db.Factory.CreateDbContextAsync();
        return await ctx.ServerLoans.AsNoTracking().Where(x => x.Uuid == uuid).Select(x => x.BorrowAmount).FirstOrDefaultAsync();
    }

    // uuid の契約を延滞させる(借りたお金を使い切って定期返済を失敗させる)。
    private static async Task MakeOverdueAsync(Env env, string uuid)
    {
        (await env.Loan.BorrowAsync(uuid, 500m)).IsSuccess.Should().BeTrue();
        (await env.Bank.WithdrawAsync(new WithdrawRequest { Uuid = uuid, Amount = 500m, PluginName = "test", Note = "spend", DisplayNote = "支出", Server = "dev" }))
            .IsSuccess.Should().BeTrue();
        await env.Loan.RunWeeklyRepayForAllAsync();
        (await env.Loan.HasOverdueDebtAsync(uuid)).Data.Should().BeTrue();
    }

    [Fact(DisplayName = "申請→承認: 承認されるまでは共有されず、承認されると参加者の残債が本垢の契約へ移る")]
    public async Task RequestThenApprove_ShouldMoveDebtAndShareContract()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await env.Loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();

        var requested = await env.Loan.RequestLinkAsync(Member, Owner);

        requested.IsSuccess.Should().BeTrue();
        requested.Data!.OwnerUuid.Should().Be(Member);
        requested.Data.OutgoingRequest!.OwnerUuid.Should().Be(Owner);
        requested.Data.OutgoingRequest.OwnerPlayer.Should().Be("owner");
        (await env.Loan.GetByUuidAsync(Member)).Data!.Uuid.Should().Be(Member);
        (await env.Loan.GetContractAsync(Owner)).Data!.PendingRequests.Select(x => (x.Uuid, x.Player, x.LinkedBy))
            .Should().Equal((Member, "member", "member"));

        var approved = await env.Loan.ApproveLinkAsync(Owner, Member);

        approved.IsSuccess.Should().BeTrue();
        approved.Data!.Members.Select(x => x.Uuid).Should().Equal(Member);
        approved.Data.PendingRequests.Should().BeEmpty();
        (await DebtAsync(env, Owner)).Should().Be(800m);
        (await DebtAsync(env, Member)).Should().Be(0m);
        (await env.Loan.GetByUuidAsync(Member)).Data!.Should().Match<ServerLoan>(l => l.Uuid == Owner && l.BorrowAmount == 800m);
        (await LogsAsync(env, Member)).Last().Should().Match<ServerLoanLog>(l =>
            l.Action == "SetBorrowAmount" && l.Amount == -300m && l.DebtAfter == 0m);
        (await LogsAsync(env, Owner)).Last().Should().Match<ServerLoanLog>(l =>
            l.Action == "MemberJoin" && l.Amount == 300m && l.ActorUuid == Member && l.DebtAfter == 800m);

        var contract = (await env.Loan.GetContractAsync(Member)).Data!;
        contract.OwnerUuid.Should().Be(Owner);
        contract.OwnerPlayer.Should().Be("owner");
        contract.Members.Should().ContainSingle(x => x.Uuid == Member && x.ApprovedAt != null);
        contract.OutgoingRequest.Should().BeNull();
    }

    [Fact(DisplayName = "承認: 支払額が未設定の契約に残債が移ってきたら、支払額の初期値を設定する")]
    public async Task Approve_OwnerWithoutPaymentAmount_ShouldSetInitialPaymentAmount()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Member, 1000m)).IsSuccess.Should().BeTrue();
        (await env.Loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();

        (await env.Loan.ApproveLinkAsync(Owner, Member)).IsSuccess.Should().BeTrue();

        // 1000 × 日利 1% × 14 日
        (await env.Loan.GetByUuidAsync(Owner)).Data!.PaymentAmount.Should().Be(140m);
    }

    [Fact(DisplayName = "申請: 自分自身・未登録の相手・二重申請・参加者がいる本垢からの申請は拒否する")]
    public async Task Request_InvalidCases_ShouldFail()
    {
        using var env = Build();

        (await env.Loan.RequestLinkAsync(Member, Member)).Code.Should().Be(ErrorCode.LinkInvalidTarget);
        (await env.Loan.RequestLinkAsync(Member, Unknown)).Code.Should().Be(ErrorCode.PlayerNotFound);
        (await env.Loan.RequestLinkAsync(Unknown, Owner)).Code.Should().Be(ErrorCode.PlayerNotFound);

        (await env.Loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();
        (await env.Loan.RequestLinkAsync(Member, Other)).Code.Should().Be(ErrorCode.LinkAlreadyLinked);

        (await env.Loan.ApproveLinkAsync(Owner, Member)).IsSuccess.Should().BeTrue();
        (await env.Loan.RequestLinkAsync(Owner, Other)).Code.Should().Be(ErrorCode.LinkHasMembers);
        // 自分の契約の参加者を紐付け先にすると、自分の契約に解決されるので拒否する。
        (await env.Loan.RequestLinkAsync(Owner, Member)).Code.Should().Be(ErrorCode.LinkInvalidTarget);
    }

    [Fact(DisplayName = "申請: 参加者を紐付け先にすると、その本垢の契約への申請になる")]
    public async Task Request_TargetIsMember_ShouldResolveToOwner()
    {
        using var env = Build();
        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();

        var res = await env.Loan.RequestLinkAsync(Other, Member);

        res.Data!.OutgoingRequest!.OwnerUuid.Should().Be(Owner);
        (await env.Loan.ApproveLinkAsync(Member, Other)).Code.Should().Be(ErrorCode.LinkRequestNotFound);
        (await env.Loan.ApproveLinkAsync(Owner, Other)).Data!.Members.Select(x => x.Uuid).Should().Equal(Member, Other);
    }

    [Fact(DisplayName = "申請・承認: どちらかの契約が延滞中はできない")]
    public async Task RequestAndApprove_WhenOverdue_ShouldFail()
    {
        using var env = Build();
        (await env.Loan.RequestLinkAsync(Other, Owner)).IsSuccess.Should().BeTrue();
        await MakeOverdueAsync(env, Owner);

        (await env.Loan.RequestLinkAsync(Member, Owner)).Code.Should().Be(ErrorCode.LinkBlockedByOverdue);
        (await env.Loan.RequestLinkAsync(Owner, Member)).Code.Should().Be(ErrorCode.LinkBlockedByOverdue);
        (await env.Loan.ApproveLinkAsync(Owner, Other)).Code.Should().Be(ErrorCode.LinkBlockedByOverdue);
    }

    [Fact(DisplayName = "承認: 申請が無ければ失敗する。拒否・取り消しで申請は消える")]
    public async Task Approve_Reject_Cancel_ShouldRequirePendingRequest()
    {
        using var env = Build();

        (await env.Loan.ApproveLinkAsync(Owner, Member)).Code.Should().Be(ErrorCode.LinkRequestNotFound);
        (await env.Loan.RejectLinkAsync(Owner, Member)).Code.Should().Be(ErrorCode.LinkRequestNotFound);
        (await env.Loan.CancelLinkRequestAsync(Member)).Code.Should().Be(ErrorCode.LinkRequestNotFound);

        (await env.Loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();
        (await env.Loan.RejectLinkAsync(Owner, Member)).Data!.PendingRequests.Should().BeEmpty();
        (await env.Loan.ApproveLinkAsync(Owner, Member)).Code.Should().Be(ErrorCode.LinkRequestNotFound);

        (await env.Loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();
        (await env.Loan.CancelLinkRequestAsync(Member)).Data!.OutgoingRequest.Should().BeNull();
        (await env.Loan.GetContractAsync(Owner)).Data!.PendingRequests.Should().BeEmpty();
    }

    [Fact(DisplayName = "脱退: 契約に残債がある間はできず、完済すると自分の契約に戻る")]
    public async Task Leave_ShouldRequireNoDebt()
    {
        using var env = Build();
        (await env.Loan.LeaveLinkAsync(Member)).Code.Should().Be(ErrorCode.LinkNotMember);
        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();
        (await env.Loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();

        (await env.Loan.LeaveLinkAsync(Member)).Code.Should().Be(ErrorCode.LinkDebtRemaining);

        (await env.Loan.RepayAsync(Member, 300m)).IsSuccess.Should().BeTrue();
        var left = await env.Loan.LeaveLinkAsync(Member);

        left.IsSuccess.Should().BeTrue();
        left.Data!.OwnerUuid.Should().Be(Member);
        (await env.Loan.GetContractAsync(Owner)).Data!.Members.Should().BeEmpty();
        (await LogsAsync(env, Owner)).Last().Should().Match<ServerLoanLog>(l =>
            l.Action == "MemberLeave" && l.Amount == 0m && l.ActorUuid == Member);
    }

    [Fact(DisplayName = "強制リンク: 延滞中でも申請なしで参加させ、承認待ちの申請は置き換える")]
    public async Task ForceLink_ShouldJoinImmediatelyEvenWhenOverdue()
    {
        using var env = Build();
        await MakeOverdueAsync(env, Owner);
        (await env.Loan.RequestLinkAsync(Member, Other)).IsSuccess.Should().BeTrue();

        var res = await env.Loan.ForceLinkAsync(Member, Owner, "admin");

        res.IsSuccess.Should().BeTrue();
        res.Data!.OwnerUuid.Should().Be(Owner);
        res.Data.Members.Should().ContainSingle(x => x.Uuid == Member && x.LinkedBy == "admin" && x.ApprovedAt != null);
        (await env.Loan.GetContractAsync(Other)).Data!.PendingRequests.Should().BeEmpty();
        (await env.Loan.HasOverdueDebtAsync(Member)).Data.Should().BeTrue();
    }

    [Fact(DisplayName = "強制リンク: 自分自身・未登録・参加済み・参加者がいる本垢は拒否する")]
    public async Task ForceLink_InvalidCases_ShouldFail()
    {
        using var env = Build();

        (await env.Loan.ForceLinkAsync(Member, Member, "admin")).Code.Should().Be(ErrorCode.LinkInvalidTarget);
        (await env.Loan.ForceLinkAsync(Unknown, Owner, "admin")).Code.Should().Be(ErrorCode.PlayerNotFound);
        (await env.Loan.ForceLinkAsync(Member, Unknown, "admin")).Code.Should().Be(ErrorCode.PlayerNotFound);

        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();
        (await env.Loan.ForceLinkAsync(Member, Other, "admin")).Code.Should().Be(ErrorCode.LinkAlreadyLinked);
        (await env.Loan.ForceLinkAsync(Owner, Other, "admin")).Code.Should().Be(ErrorCode.LinkHasMembers);
    }

    [Theory(DisplayName = "強制リンク: resetLimit を指定したときだけ、参加先の契約の借入枠を初期枠に戻す")]
    [InlineData(false, 1100)]
    [InlineData(true, 1000)]
    public async Task ForceLink_ResetLimit_ShouldResetOwnersLimit(bool resetLimit, int expectedLimit)
    {
        using var env = Build();
        // 本垢が 1000 を 1 週間借りて全額返した履歴を作り、枠を 1000 → 1100 に育てておく(増加は枠の 10% まで)。
        await using (var ctx = await env.Db.Factory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            ctx.ServerLoanLogs.Add(new ServerLoanLog { Uuid = Owner, Player = "owner", Action = "Borrow", Amount = 1000m, Date = now.AddDays(-8) });
            for (var day = 7; day >= 1; day--)
                ctx.ServerLoanLogs.Add(new ServerLoanLog { Uuid = Owner, Player = "owner", Action = "Interest", Amount = 0m, Date = now.AddDays(-day) });
            ctx.ServerLoanLogs.Add(new ServerLoanLog { Uuid = Owner, Player = "owner", Action = "RepaySuccess", Amount = -1000m, Date = now.AddDays(-0.5) });
            await ctx.SaveChangesAsync();
        }
        (await env.Loan.CalculateBorrowLimitAsync(Owner)).Data.Should().Be(1100m);

        (await env.Loan.ForceLinkAsync(Member, Owner, "admin", resetLimit)).IsSuccess.Should().BeTrue();

        (await env.Loan.CalculateBorrowLimitAsync(Member)).Data.Should().Be(expectedLimit);
        (await LogsAsync(env, Owner)).Any(l => l.Action == "LimitReset").Should().Be(resetLimit);
    }

    [Fact(DisplayName = "強制解除: 残債があっても外せて、移した残債は本垢の契約に残る")]
    public async Task ForceUnlink_ShouldKeepDebtOnOwnersContract()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();
        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();

        var res = await env.Loan.ForceUnlinkAsync(Member, "admin");

        res.IsSuccess.Should().BeTrue();
        res.Data!.OwnerUuid.Should().Be(Member);
        (await DebtAsync(env, Owner)).Should().Be(300m);
        (await env.Loan.GetByUuidAsync(Member)).Data!.Should().Match<ServerLoan>(l => l.Uuid == Member && l.BorrowAmount == 0m);
        (await LogsAsync(env, Owner)).Select(l => l.Action).Should().Equal("MemberJoin", "MemberLeave");
        (await env.Loan.ForceUnlinkAsync(Member, "admin")).Code.Should().Be(ErrorCode.LinkNotMember);
    }

    [Fact(DisplayName = "強制解除: 承認待ちの申請も取り消せる")]
    public async Task ForceUnlink_PendingRequest_ShouldRemoveIt()
    {
        using var env = Build();
        (await env.Loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();

        (await env.Loan.ForceUnlinkAsync(Member, "admin")).IsSuccess.Should().BeTrue();

        (await env.Loan.GetContractAsync(Owner)).Data!.PendingRequests.Should().BeEmpty();
        (await LogsAsync(env, Owner)).Should().BeEmpty();
    }

    [Fact(DisplayName = "借入枠: 参加・脱退のログは枠の算定に影響しない")]
    public async Task BorrowLimit_ShouldIgnoreJoinAndLeaveLogs()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();
        (await env.Loan.ForceLinkAsync(Member, Owner, "admin")).IsSuccess.Should().BeTrue();
        (await env.Loan.ForceUnlinkAsync(Member, "admin")).IsSuccess.Should().BeTrue();

        (await env.Loan.CalculateBorrowLimitAsync(Owner)).Data.Should().Be(1000m);
        (await env.Loan.CalculateBorrowLimitAsync(Member)).Data.Should().Be(1000m);
    }
}
