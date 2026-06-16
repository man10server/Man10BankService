using FluentAssertions;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Controllers;

// 送金の並行整合性検証(DESIGN 4.1)。MySQL(Testcontainers)上で行ロックの効果を確認する。
[Collection(MySqlCollection.Name)]
public class BankTransferConcurrencyMySqlTests
{
    private const string FromUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string ToUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    [Fact(DisplayName = "transfer(MySQL): 並行送金でも送金元の残高はマイナスにならず、両者残高+ログ合計が整合する")]
    public async Task ConcurrentTransfers_ShouldStayConsistent()
    {
        using var db = MySqlTestDbFactory.Create();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);

        // 送金元に 1000 を用意(各送金は 100。送金は10件中、成功できるのは10件=1000ちょうど)
        (await bank.DepositAsync(new DepositRequest
        {
            Uuid = FromUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();

        // 20件を並行投入。残高は1000なので 100×10件のみ成功、残りは残高不足(409)になるはず。
        var tasks = Enumerable.Range(0, 20).Select(_ => bank.TransferAsync(new TransferRequest
        {
            FromUuid = FromUuid,
            ToUuid = ToUuid,
            Amount = 100m,
            PluginName = "test",
            Note = "transfer",
            DisplayNote = "並行送金",
            Server = "dev"
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        var success = results.Count(r => r.IsSuccess);
        var insufficient = results.Count(r => !r.IsSuccess && r.Code == ErrorCode.InsufficientFunds);

        success.Should().Be(10);
        insufficient.Should().Be(10);

        // 送金元は0、送金先は1000(マイナス突き抜けなし)
        (await bank.GetBalanceAsync(FromUuid)).Data.Should().Be(0m);
        (await bank.GetBalanceAsync(ToUuid)).Data.Should().Be(1000m);

        // ログ合計と残高の整合(送金元: 出金ログのみ、送金先: 入金ログのみ)
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var fromSum = await ctx.MoneyLogs.AsNoTracking().Where(x => x.Uuid == FromUuid).SumAsync(x => x.Amount);
        var toSum = await ctx.MoneyLogs.AsNoTracking().Where(x => x.Uuid == ToUuid).SumAsync(x => x.Amount);
        fromSum.Should().Be(0m);   // +1000(seed) - 1000(送金10件)
        toSum.Should().Be(1000m);  // +1000(送金10件)

        // 成功送金1件につき MoneyLog 2件(出金1+入金1)
        var transferLogCount = await ctx.MoneyLogs.AsNoTracking().CountAsync(x => x.Note == "transfer");
        transferLogCount.Should().Be(20);
    }
}
