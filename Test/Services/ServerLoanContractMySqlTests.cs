using FluentAssertions;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Services;

// サーバーローンの契約(server_loan_tbl)が 1 プレイヤーにつき 1 行であることの検証。
// 同時作成の競合は一意制約で弾くため、本番と同じ MySQL(Testcontainers)上で確認する。
[Collection(MySqlCollection.Name)]
public class ServerLoanContractMySqlTests
{
    private const string Uuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";

    [Fact(DisplayName = "契約(MySQL): 同時に初回取得しても行は1つだけ作られ、全員が同じ契約を受け取る")]
    public async Task GetByUuid_ConcurrentFirstAccess_ShouldCreateSingleContract()
    {
        using var db = MySqlTestDbFactory.Create();
        var repo = new ServerLoanRepository(db.Factory, new FakePlayerProfileService());

        var loans = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => repo.GetOrCreateByUuidAsync(Uuid))));

        loans.Select(l => l.Id).Distinct().Should().HaveCount(1);

        await using var ctx = await db.Factory.CreateDbContextAsync();
        (await ctx.ServerLoans.CountAsync(x => x.Uuid == Uuid)).Should().Be(1);
    }
}
