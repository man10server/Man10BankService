using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Microsoft.Extensions.Configuration;
using Test.Infrastructure;

namespace Test.Services;

[Collection(ServerLoanConfigCollection.Name)]
public class ServerLoanOverdueBlockTests
{
    private const string Uuid = TestConstants.Uuid;
    private const string OtherUuid = TestConstants.BorrowUuid;

    private sealed record Env(ServerLoanService Loan, BankService Bank, ChequeService Cheque, LoanService UserLoan, TestDbFactory Db) : IDisposable
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
                ["ServerLoan:MinPaymentAmount"] = "0"
            })
            .Build();
        var profile = new FakePlayerProfileService();
        var bank = new BankService(db.Factory, profile);
        var loan = new ServerLoanService(db.Factory, bank, profile, config);
        return new Env(loan, bank, new ChequeService(db.Factory, bank), new LoanService(db.Factory, bank, profile), db);
    }

    private static Task<ApiResult<decimal>> DepositAsync(BankService bank, string uuid, decimal amount) =>
        bank.DepositAsync(new DepositRequest { Uuid = uuid, Amount = amount, PluginName = "test", Note = "seed", DisplayNote = "seed", Server = "dev" });

    private static Task<ApiResult<decimal>> WithdrawAsync(BankService bank, decimal amount) =>
        bank.WithdrawAsync(new WithdrawRequest { Uuid = Uuid, Amount = amount, PluginName = "test", Note = "spend", DisplayNote = "支出", Server = "dev" });

    private static Task<ApiResult<decimal>> TransferAsync(BankService bank, string from, string to, decimal amount) =>
        bank.TransferAsync(new TransferRequest { FromUuid = from, ToUuid = to, Amount = amount, PluginName = "test", Note = "pay", DisplayNote = "送金", Server = "dev" });

    // 500 円借りて残高を空にし、週次返済を失敗させたうえで 1000 円入金する(失敗額は 70)。
    private static async Task MakeOverdueWithBalanceAsync(Env env)
    {
        (await env.Loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();
        (await WithdrawAsync(env.Bank, 500m)).IsSuccess.Should().BeTrue();
        await env.Loan.RunWeeklyRepayForAllAsync();
        (await DepositAsync(env.Bank, Uuid, 1000m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞中: 出金・送金・小切手作成・貸付は拒否され残高は変わらない")]
    public async Task Overdue_ShouldBlockOutflows()
    {
        using var env = Build();
        await MakeOverdueWithBalanceAsync(env);

        (await WithdrawAsync(env.Bank, 100m)).Code.Should().Be(ErrorCode.WithdrawBlockedByOverdue);
        (await TransferAsync(env.Bank, Uuid, OtherUuid, 100m)).Code.Should().Be(ErrorCode.WithdrawBlockedByOverdue);
        (await env.Cheque.CreateAsync(new ChequeCreateRequest { Uuid = Uuid, Amount = 100m, Note = "test" }))
            .Code.Should().Be(ErrorCode.WithdrawBlockedByOverdue);
        (await env.UserLoan.CreateAsync(new LoanCreateRequest
        {
            LendUuid = Uuid,
            BorrowUuid = OtherUuid,
            BorrowAmount = 100m,
            RepayAmount = 110m,
            PaybackDate = DateTime.UtcNow.AddDays(7)
        })).Code.Should().Be(ErrorCode.WithdrawBlockedByOverdue);

        (await env.Bank.GetBalanceAsync(Uuid)).Data.Should().Be(1000m);
    }

    [Fact(DisplayName = "延滞中: 入金・送金の受け取り・手動返済・運営小切手の作成はできる")]
    public async Task Overdue_ShouldAllowInflowsAndRepay()
    {
        using var env = Build();
        await MakeOverdueWithBalanceAsync(env);
        (await DepositAsync(env.Bank, OtherUuid, 500m)).IsSuccess.Should().BeTrue();

        (await DepositAsync(env.Bank, Uuid, 100m)).IsSuccess.Should().BeTrue();
        (await TransferAsync(env.Bank, OtherUuid, Uuid, 100m)).IsSuccess.Should().BeTrue();
        (await env.Loan.RepayAsync(Uuid, 100m)).IsSuccess.Should().BeTrue();
        (await env.Cheque.CreateAsync(new ChequeCreateRequest { Uuid = Uuid, Amount = 100m, Note = "test", Op = true }))
            .IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞中: IgnoreOverdueBlock を指定した出金は通る")]
    public async Task Overdue_WithdrawWithIgnoreOverdueBlock_ShouldSucceed()
    {
        using var env = Build();
        await MakeOverdueWithBalanceAsync(env);

        var res = await env.Bank.WithdrawAsync(new WithdrawRequest
        {
            Uuid = Uuid,
            Amount = 100m,
            PluginName = "test",
            Note = "admin_take",
            DisplayNote = "運営による調整",
            Server = "dev",
            IgnoreOverdueBlock = true
        });

        res.IsSuccess.Should().BeTrue();
        res.Data.Should().Be(900m);
    }

    [Fact(DisplayName = "延滞中: 延滞分を回収しきると出金できるようになる")]
    public async Task Overdue_AfterCollection_ShouldAllowWithdraw()
    {
        using var env = Build();
        await MakeOverdueWithBalanceAsync(env);

        await env.Loan.RunOverdueCollectionForAllAsync();

        (await WithdrawAsync(env.Bank, 100m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞中: 手動で完済して残債がなければ出金できる")]
    public async Task Overdue_AfterFullManualRepay_ShouldAllowWithdraw()
    {
        using var env = Build();
        await MakeOverdueWithBalanceAsync(env);

        (await env.Loan.RepayAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        (await WithdrawAsync(env.Bank, 100m)).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "延滞していなければ残債があっても出金できる")]
    public async Task NotOverdue_WithDebt_ShouldAllowWithdraw()
    {
        using var env = Build();
        (await env.Loan.BorrowAsync(Uuid, 500m)).IsSuccess.Should().BeTrue();

        (await WithdrawAsync(env.Bank, 100m)).IsSuccess.Should().BeTrue();
    }
}
