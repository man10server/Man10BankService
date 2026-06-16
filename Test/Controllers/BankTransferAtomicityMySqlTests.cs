using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Controllers;

// 送金の原子性検証(DESIGN 4.1): 片側(送金先)の書き込みが失敗した場合に、
// すでに適用済みの送金元出金も含めて両方ロールバックされること。
// 送金先の player 名を varchar(16) 超にして MySQL(strict mode)で SaveChanges を
// 失敗させ、トランザクション全体が巻き戻ることを確認する。
// varchar 長制約は SQLite では強制されないため、本テストは MySQL 専用とする。
[Collection(MySqlCollection.Name)]
public class BankTransferAtomicityMySqlTests
{
    private const string FromUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string ToUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    // 送金先(ToUuid)の名前解決だけ16文字超の名前を返し、user_bank への INSERT を失敗させる。
    private sealed class OversizedReceiverProfileService : IPlayerProfileService
    {
        public Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
        {
            if (uuid == ToUuid)
                return Task.FromResult<string?>(new string('x', 32)); // varchar(16) 超
            return Task.FromResult<string?>("from-player");
        }
    }

    [Fact(DisplayName = "transfer(MySQL): 送金先の書き込み失敗時は送金元出金も含めて両方ロールバックされる")]
    public async Task Transfer_ReceiverWriteFails_ShouldRollbackBoth()
    {
        using var db = MySqlTestDbFactory.Create();
        var profile = new OversizedReceiverProfileService();
        var bank = new BankService(db.Factory, profile);

        // 送金元に 1000 を用意(正常名で seed する)
        var normalProfile = new FakePlayerProfileService("seed-player");
        var seedBank = new BankService(db.Factory, normalProfile);
        (await seedBank.DepositAsync(new DepositRequest
        {
            Uuid = FromUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();

        // 送金実行: 送金先 user_bank の INSERT が player 名超過で失敗する。
        var res = await bank.TransferAsync(new TransferRequest
        {
            FromUuid = FromUuid,
            ToUuid = ToUuid,
            Amount = 300m,
            PluginName = "test",
            Note = "transfer",
            DisplayNote = "原子性テスト",
            Server = "dev"
        });

        // 例外は RunExclusiveAsync で握られ UnexpectedError として返る。
        res.IsSuccess.Should().BeFalse();
        res.Code.Should().Be(ErrorCode.UnexpectedError);

        // 送金元残高は変化なし(出金がロールバックされている)
        (await bank.GetBalanceAsync(FromUuid)).Data.Should().Be(1000m);
        // 送金先口座は作成されていない
        (await bank.GetBalanceAsync(ToUuid)).Data.Should().Be(0m);

        // 送金ログ(出金・入金とも)が1件も残らない
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var transferLogs = await ctx.MoneyLogs.AsNoTracking().CountAsync(x => x.Note == "transfer");
        transferLogs.Should().Be(0);
    }
}
