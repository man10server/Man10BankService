using FluentAssertions;
using Man10BankService.Controllers;
using Man10BankService.Data;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Models.Responses;
using Man10BankService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Test.Infrastructure;

namespace Test.Controllers;

// 小切手 Use の原子性検証(DESIGN 4.1): 受取人入金と Used 更新が同一トランザクションで成立する。
public class ChequeAtomicityTests
{
    private const string IssuerUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private const string ReceiverUuid = "49c42256-2357-4963-8678-7a06e6dd3125";

    private sealed record TestEnv(ControllerHost Host, ChequeService Cheque, BankService Bank, IDbContextFactory<BankDbContext> DbFactory) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    private static TestEnv Build()
    {
        var db = TestDbFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(ChequesController).Assembly);
        var sp = services.BuildServiceProvider();

        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var cheque = new ChequeService(db.Factory, bank, profile);
        var ctrl = new ChequesController(cheque)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ObjectValidator = sp.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = sp.GetRequiredService<IModelMetadataProvider>()
        };
        var host = new ControllerHost { Controller = ctrl, Resources = [db, sp] };
        return new TestEnv(host, cheque, bank, db.Factory);
    }

    [Fact(DisplayName = "小切手Use: 受取人残高加算とUsed更新が原子的に成立する")]
    public async Task Use_ShouldDepositReceiver_AndMarkUsed_Atomically()
    {
        using var env = Build();
        var ctrl = (ChequesController)env.Host.Controller;

        // 発行者に残高を用意し、op=false で小切手を発行(発行者残高が減る)
        (await env.Bank.DepositAsync(new DepositRequest
        {
            Uuid = IssuerUuid, Amount = 500m, PluginName = "test", Note = "seed", DisplayNote = "初期", Server = "dev"
        })).IsSuccess.Should().BeTrue();

        var created = await ctrl.Create(new ChequeCreateRequest { Uuid = IssuerUuid, Amount = 500m, Note = "gift" });
        var id = created.Result
            .Should().BeOfType<CreatedAtActionResult>().Which.Value
            .Should().BeOfType<ChequeResponse>().Which.Id;

        // 発行で発行者残高は 0 に
        (await env.Bank.GetBalanceAsync(IssuerUuid)).Data.Should().Be(0m);

        // 受取人が使用 → 受取人残高 +500 かつ Used=true
        var use = await ctrl.Use(id, new ChequeUseRequest { Uuid = ReceiverUuid });
        var used = use.Result
            .Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<ChequeResponse>().Which;
        used.Used.Should().BeTrue();

        (await env.Bank.GetBalanceAsync(ReceiverUuid)).Data.Should().Be(500m);

        // 受取人の入金ログが1件・小切手は使用済みで永続化されている
        await using var ctx = await env.DbFactory.CreateDbContextAsync();
        var receiverLogs = await ctx.MoneyLogs.AsNoTracking()
            .Where(x => x.Uuid == ReceiverUuid && x.Note == $"cheque_use:{id}").ToListAsync();
        receiverLogs.Should().HaveCount(1);
        receiverLogs[0].Amount.Should().Be(500m);

        var persisted = await ctx.Cheques.AsNoTracking().FirstAsync(x => x.Id == id);
        persisted.Used.Should().BeTrue();
        persisted.UsePlayer.Should().NotBeNullOrEmpty();
    }
}
