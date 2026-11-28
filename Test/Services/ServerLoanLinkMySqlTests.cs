using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 契約の共有(リンク)の一連の流れが、本番と同じ MySQL(行ロックあり)でも成り立つことの検証。
[Collection(MySqlCollection.Name)]
public class ServerLoanLinkMySqlTests
{
    private const string Owner = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string Member = "11111111-1111-1111-1111-111111111111";

    [Fact(DisplayName = "共有(MySQL): 申請→承認→定期返済→完済→脱退が一通り動く")]
    public async Task LinkLifecycle_ShouldWorkOnMySql()
    {
        using var db = MySqlTestDbFactory.Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServerLoan:DailyInterestRate"] = "0.01",
                ["ServerLoan:MinAmount"] = "1000",
                ["ServerLoan:MinPaymentRate"] = "0",
                ["ServerLoan:MinPaymentAmount"] = "0",
                ["ServerLoan:MinPrincipalRate"] = "0"
            })
            .Build();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);

        (await loan.BorrowAsync(Owner, 500m)).IsSuccess.Should().BeTrue();
        (await loan.BorrowAsync(Member, 300m)).IsSuccess.Should().BeTrue();
        (await loan.RequestLinkAsync(Member, Owner)).IsSuccess.Should().BeTrue();
        (await loan.ApproveLinkAsync(Owner, Member)).IsSuccess.Should().BeTrue();
        (await loan.GetByUuidAsync(Member)).Data!.BorrowAmount.Should().Be(800m);

        // 本垢の口座を 100 にして、定期返済 400 を本垢 100 + メンバー 300 で引き落とす。
        (await bank.WithdrawAsync(new WithdrawRequest { Uuid = Owner, Amount = 400m, PluginName = "test", Note = "spend", DisplayNote = "支出", Server = "dev" }))
            .IsSuccess.Should().BeTrue();
        (await loan.SetPaymentAmountAsync(Member, 400m)).IsSuccess.Should().BeTrue();
        await loan.RunWeeklyRepayForAllAsync();

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            (await ctx.UserBanks.AsNoTracking().Where(x => x.Uuid == Owner).Select(x => x.Balance).SingleAsync()).Should().Be(0m);
            (await ctx.UserBanks.AsNoTracking().Where(x => x.Uuid == Member).Select(x => x.Balance).SingleAsync()).Should().Be(0m);
            (await ctx.ServerLoans.AsNoTracking().Where(x => x.Uuid == Owner).Select(x => x.BorrowAmount).SingleAsync()).Should().Be(400m);
        }

        (await loan.LeaveLinkAsync(Member)).Code.Should().Be(ErrorCode.LinkDebtRemaining);
        (await bank.DepositAsync(new DepositRequest { Uuid = Member, Amount = 400m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" }))
            .IsSuccess.Should().BeTrue();
        (await loan.RepayAsync(Member, 400m)).IsSuccess.Should().BeTrue();
        (await loan.LeaveLinkAsync(Member)).IsSuccess.Should().BeTrue();

        (await loan.GetByUuidAsync(Member)).Data!.Uuid.Should().Be(Member);
        await using (var ctx = await db.Factory.CreateDbContextAsync())
            (await ctx.ServerLoanMembers.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
