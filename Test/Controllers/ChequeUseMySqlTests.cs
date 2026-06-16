using System.Collections.Concurrent;
using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.EntityFrameworkCore;
using Test.Infrastructure;

namespace Test.Controllers;

// 小切手使用の並行整合性・原子性検証(DESIGN 4.1 / 2.3)。本番と同じ MySQL(Testcontainers)上で、
// (1) 同一小切手を多数同時に使用しても cheque 行ロックにより成功は1件のみで二重入金されないこと、
// (2) 受取人入金が失敗した場合に Used 更新も巻き戻り小切手が未使用のまま残ること、を確認する。
[Collection(MySqlCollection.Name)]
public class ChequeUseMySqlTests
{
    private const string IssuerUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string ReceiverUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    [Fact(DisplayName = "小切手使用(MySQL): 並行使用でも成功は1件のみで二重入金されない")]
    public async Task ConcurrentUse_ShouldAllowOnlyFirst_NoDoubleCredit()
    {
        using var db = MySqlTestDbFactory.Create();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var cheque = new ChequeService(db.Factory, bank, profile);

        // 発行者残高を用意し op=false で小切手を発行(額面1000)
        (await bank.DepositAsync(new DepositRequest
        {
            Uuid = IssuerUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();

        var created = await cheque.CreateAsync(new ChequeCreateRequest { Uuid = IssuerUuid, Amount = 1000m, Note = "race" });
        created.IsSuccess.Should().BeTrue();
        var id = created.Data!.Id;

        // 同一小切手を10並行で使用
        var results = new ConcurrentBag<ErrorCode>();
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var res = await cheque.UseAsync(id, new ChequeUseRequest { Uuid = ReceiverUuid });
            results.Add(res.IsSuccess ? ErrorCode.None : res.Code);
        }).ToArray();
        await Task.WhenAll(tasks);

        // 成功は1件、残りは使用済み(409)
        results.Count(c => c == ErrorCode.None).Should().Be(1);
        results.Count(c => c == ErrorCode.ChequeAlreadyUsed).Should().Be(9);

        // 受取人残高は額面ちょうど(二重入金なし)
        (await bank.GetBalanceAsync(ReceiverUuid)).Data.Should().Be(1000m);

        // cheque_use の入金ログは1件のみ
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var useLogs = await ctx.MoneyLogs.AsNoTracking()
            .CountAsync(x => x.Uuid == ReceiverUuid && x.Note == $"cheque_use:{id}");
        useLogs.Should().Be(1);
    }

    // 受取人(ReceiverUuid)の名前解決だけ16文字超を返し、user_bank への INSERT を失敗させる。
    private sealed class OversizedReceiverProfileService : IPlayerProfileService
    {
        public Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
        {
            if (uuid == ReceiverUuid)
                return Task.FromResult<string?>(new string('x', 32)); // varchar(16) 超
            return Task.FromResult<string?>("issuer-player");
        }
    }

    [Fact(DisplayName = "小切手使用(MySQL): 受取人入金が失敗するとUsed更新も巻き戻り小切手は未使用のまま")]
    public async Task Use_ReceiverWriteFails_ShouldKeepChequeUnused()
    {
        using var db = MySqlTestDbFactory.Create();

        // 発行は正常名で行う(発行者残高を引き落として小切手を作る)
        var normalProfile = new FakePlayerProfileService("issuer");
        var normalBank = new BankService(db.Factory, normalProfile);
        var normalCheque = new ChequeService(db.Factory, normalBank, normalProfile);
        (await normalBank.DepositAsync(new DepositRequest
        {
            Uuid = IssuerUuid, Amount = 1000m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();
        var created = await normalCheque.CreateAsync(new ChequeCreateRequest { Uuid = IssuerUuid, Amount = 1000m, Note = "atomic" });
        created.IsSuccess.Should().BeTrue();
        var id = created.Data!.Id;

        // 使用は受取人入金が失敗するプロファイルで実行
        var badProfile = new OversizedReceiverProfileService();
        var badBank = new BankService(db.Factory, badProfile);
        var badCheque = new ChequeService(db.Factory, badBank, badProfile);
        var res = await badCheque.UseAsync(id, new ChequeUseRequest { Uuid = ReceiverUuid });

        // 例外は RunExclusiveAsync で握られ UnexpectedError として返る
        res.IsSuccess.Should().BeFalse();
        res.Code.Should().Be(ErrorCode.UnexpectedError);

        // 小切手は未使用のまま(Used 更新がロールバックされている)
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var persisted = await ctx.Cheques.AsNoTracking().FirstAsync(x => x.Id == id);
        persisted.Used.Should().BeFalse();

        // 受取人口座は作成されず、入金ログも残らない
        (await normalBank.GetBalanceAsync(ReceiverUuid)).Data.Should().Be(0m);
        var useLogs = await ctx.MoneyLogs.AsNoTracking()
            .CountAsync(x => x.Uuid == ReceiverUuid && x.Note == $"cheque_use:{id}");
        useLogs.Should().Be(0);
    }
}
