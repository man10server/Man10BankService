using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

// 旧リボの一連の流れが、本番と同じ MySQL(行ロックあり・本番のスキーマ)でも成り立つことの検証。
[Collection(MySqlCollection.Name)]
public class LegacyServerLoanMySqlTests
{
    private const string Player = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    [Fact(DisplayName = "旧リボ(MySQL): 借入の停止→定期返済→手動返済で完済→新リボの借入が一通り動く")]
    public async Task LegacyLifecycle_ShouldWorkOnMySql()
    {
        using var db = MySqlTestDbFactory.Create();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServerLoan:MinAmount"] = "1000" })
            .Build();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);
        var legacy = new LegacyServerLoanService(db.Factory, bank, profile);

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.LegacyServerLoans.Add(new LegacyServerLoan { Uuid = Player, Player = "player", BorrowAmount = 500m, PaymentAmount = 100m, LastPayDate = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        (await bank.DepositAsync(new DepositRequest { Uuid = Player, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" }))
            .IsSuccess.Should().BeTrue();

        (await loan.BorrowAsync(Player, 100m)).Code.Should().Be(ErrorCode.LegacyLoanOutstanding);

        await legacy.RunWeeklyRepayForAllAsync();
        (await legacy.GetAsync(Player)).Data!.BorrowAmount.Should().Be(400m);

        (await legacy.SetPaymentAmountAsync(Player, 50m)).Data!.PaymentAmount.Should().Be(50m);
        (await legacy.RepayAsync(Player, 400m)).Data!.BorrowAmount.Should().Be(0m);
        (await legacy.HasDebtAsync(Player)).Data.Should().BeFalse();

        (await loan.BorrowAsync(Player, 100m)).IsSuccess.Should().BeTrue();

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            (await ctx.UserBanks.AsNoTracking().Where(x => x.Uuid == Player).Select(x => x.Balance).SingleAsync()).Should().Be(600m);
            (await ctx.LegacyServerLoanLogs.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Action).ToListAsync())
                .Should().Equal("RepaySuccess", "EarlyRepay");
            (await ctx.ServerLoans.AsNoTracking().Where(x => x.Uuid == Player).Select(x => x.BorrowAmount).SingleAsync()).Should().Be(100m);
        }
    }
}
