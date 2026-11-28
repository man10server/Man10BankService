using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Repositories;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Services;

// サーバーローンの契約(server_loan_tbl)・ログ(server_loan_log)・メンバー(server_loan_member)が
// 本番スキーマ(db.sql)で期待どおり保存されることの検証。
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

    [Fact(DisplayName = "ログ(MySQL): 操作後の残債を記録し、操作者は自動処理では空のままにする")]
    public async Task Log_ShouldStoreDebtAfterAndNullActor()
    {
        using var db = MySqlTestDbFactory.Create();
        var repo = new ServerLoanRepository(db.Factory, new FakePlayerProfileService());

        await repo.SetBorrowAmountAsync(Uuid, 5000m, 100m);
        await repo.AdjustLoanAsync(Uuid, 50m, ServerLoanRepository.ServerLoanLogAction.Interest);

        await using var ctx = await db.Factory.CreateDbContextAsync();
        var logs = await ctx.ServerLoanLogs.AsNoTracking().Where(x => x.Uuid == Uuid).OrderBy(x => x.Id).ToListAsync();
        logs.Select(l => (l.Action, l.ActorUuid, l.DebtAfter)).Should().Equal(
            ("SetBorrowAmount", null, 5000m),
            ("Interest", null, 5050m));
    }

    [Fact(DisplayName = "契約(MySQL): failed_payment 列が残っているスキーマでも作成・行ロック・更新ができる")]
    public async Task Contract_WithLegacyFailedPaymentColumn_ShouldStillWork()
    {
        using var db = MySqlTestDbFactory.Create();
        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            // sql/011 を適用する前の状態を再現する。
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE server_loan_tbl ADD COLUMN failed_payment INT DEFAULT 0 NOT NULL");
        }
        var repo = new ServerLoanRepository(db.Factory, new FakePlayerProfileService());

        (await repo.GetOrCreateByUuidAsync(Uuid)).BorrowAmount.Should().Be(0m);
        var updated = await repo.AdjustLoanAsync(Uuid, 1000m, ServerLoanRepository.ServerLoanLogAction.Borrow);

        updated!.BorrowAmount.Should().Be(1000m);
        (await repo.GetByUuidAsync(Uuid))!.BorrowAmount.Should().Be(1000m);
    }

    [Fact(DisplayName = "メンバー(MySQL): 承認待ちで保存でき、1アカウントは1つの契約にしか参加できない")]
    public async Task Member_ShouldBePendingByDefaultAndUniquePerAccount()
    {
        const string member = "11111111-1111-1111-1111-111111111111";
        const string otherOwner = "22222222-2222-2222-2222-222222222222";
        using var db = MySqlTestDbFactory.Create();

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            ctx.ServerLoanMembers.Add(new ServerLoanMember { Uuid = member, OwnerUuid = Uuid, LinkedBy = "test-player" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = await db.Factory.CreateDbContextAsync())
        {
            var saved = await ctx.ServerLoanMembers.AsNoTracking().SingleAsync(x => x.OwnerUuid == Uuid);
            saved.Uuid.Should().Be(member);
            saved.ApprovedAt.Should().BeNull();
            saved.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            ctx.ServerLoanMembers.Add(new ServerLoanMember { Uuid = member, OwnerUuid = otherOwner, LinkedBy = "test-player" });
            await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
    }
}
