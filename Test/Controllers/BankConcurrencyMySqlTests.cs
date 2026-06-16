using System.Collections.Concurrent;
using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Controllers;

// 並行入出金の整合性検証(DESIGN 4.1)。本番と同じ MySQL(Testcontainers)上で、
// 行ロック(SELECT ... FOR UPDATE)により残高がマイナスへ突き抜けず、
// 最終残高が MoneyLog 合計と一致することを確認する。
// SQLite では行ロックが効かない(単一ライターへ直列化される)ため MySQL 専用とする。
[Collection(MySqlCollection.Name)]
public class BankConcurrencyMySqlTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    [Fact(DisplayName = "並行入出金(MySQL): 残高はマイナスにならず、最終残高がログ合計と一致する")]
    public async Task ConcurrentDepositWithdraw_ShouldStayConsistent()
    {
        using var db = MySqlTestDbFactory.Create();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);

        // 初期残高 2000 を用意
        (await bank.DepositAsync(new DepositRequest
        {
            Uuid = Uuid, Amount = 2000m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();

        var rnd = new Random(20240612);
        var operations = Enumerable.Range(0, 100)
            .Select(_ => (amount: (decimal)rnd.Next(1, 501), deposit: rnd.Next(0, 2) == 0))
            .ToArray();

        var conflictFlags = new ConcurrentBag<bool>();
        var tasks = operations.Select(async op =>
        {
            if (op.deposit)
            {
                (await bank.DepositAsync(new DepositRequest
                {
                    Uuid = Uuid, Amount = op.amount, PluginName = "test",
                    Note = $"dep{op.amount}", DisplayNote = $"入金{op.amount}", Server = "dev"
                })).IsSuccess.Should().BeTrue();
                conflictFlags.Add(false);
            }
            else
            {
                var res = await bank.WithdrawAsync(new WithdrawRequest
                {
                    Uuid = Uuid, Amount = op.amount, PluginName = "test",
                    Note = $"wd{op.amount}", DisplayNote = $"出金{op.amount}", Server = "dev"
                });
                var isConflict = !res.IsSuccess && res.Code == ErrorCode.InsufficientFunds;
                if (!isConflict)
                    res.IsSuccess.Should().BeTrue();
                conflictFlags.Add(isConflict);
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        await using var ctx = await db.Factory.CreateDbContextAsync();
        var logSum = await ctx.MoneyLogs.AsNoTracking().Where(x => x.Uuid == Uuid).SumAsync(x => x.Amount);

        var balance = (await bank.GetBalanceAsync(Uuid)).Data;
        // 最終残高はログ合計と一致し、マイナスへ突き抜けていない
        balance.Should().Be(logSum);
        balance.Should().BeGreaterThanOrEqualTo(0m);

        // 残高不足で弾かれた出金は MoneyLog に残らない(出金試行回数 - 出金ログ件数 = 409件数)
        var withdrawAttempts = operations.Count(o => !o.deposit);
        var withdrawLogs = await ctx.MoneyLogs.AsNoTracking().CountAsync(x => x.Uuid == Uuid && !x.Deposit);
        var conflicts = conflictFlags.Count(f => f);
        (withdrawAttempts - withdrawLogs).Should().Be(conflicts);
    }
}
