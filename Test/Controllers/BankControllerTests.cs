using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Man10BankService.Models.Database;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Test.Infrastructure;
using Xunit;

namespace Test.Controllers;

public class BankControllerTests : IClassFixture<MySqlFixture>
{
    private readonly ApiFactory _factory;

    public BankControllerTests(MySqlFixture fixture)
    {
        _factory = new ApiFactory(fixture);
    }

    [Fact]
    public async Task Deposit_Success_ShouldIncreaseBalance_AndWriteLog()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var req = new DepositRequest
        {
            Uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Player = "steve",
            Amount = 500,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        var res = await client.PostAsJsonAsync("/api/bank/deposit", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        body!.Data.Should().Be(500);

        var bal = await client.GetFromJsonAsync<ApiResult<decimal>>($"/api/bank/{req.Uuid}/balance");
        bal!.Data.Should().Be(500);

        var logs = await client.GetFromJsonAsync<ApiResult<List<MoneyLog>>>($"/api/bank/{req.Uuid}/logs?limit=10");
        logs!.Data!.Count.Should().BeGreaterOrEqualTo(1);
        logs.Data![0].Amount.Should().Be(500);
        logs.Data![0].Deposit.Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_Invalid_ShouldNotChangeBalance_AndReturn400()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var req = new DepositRequest
        {
            Uuid = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            Player = "alex",
            Amount = 0, // invalid
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };
        var res = await client.PostAsJsonAsync("/api/bank/deposit", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bal = await client.GetFromJsonAsync<ApiResult<decimal>>($"/api/bank/{req.Uuid}/balance");
        bal!.Data.Should().Be(0);

        var logs = await client.GetFromJsonAsync<ApiResult<List<MoneyLog>>>($"/api/bank/{req.Uuid}/logs?limit=10");
        logs!.Data!.Count.Should().Be(0);
    }

    [Fact]
    public async Task Withdraw_Success_And_Failure_Paths()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var uuid = "cccccccc-cccc-cccc-cccc-cccccccccccc";
        // seed 1000
        var seed = new DepositRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 1000,
            PluginName = "test",
            Note = "seed",
            DisplayNote = "初期入金",
            Server = "dev"
        };
        (await client.PostAsJsonAsync("/api/bank/deposit", seed)).EnsureSuccessStatusCode();

        // withdraw 600 -> OK (balance 400)
        var w1 = new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 600,
            PluginName = "test",
            Note = "w1",
            DisplayNote = "出金1",
            Server = "dev"
        };
        var res1 = await client.PostAsJsonAsync("/api/bank/withdraw", w1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var bal1 = await client.GetFromJsonAsync<ApiResult<decimal>>($"/api/bank/{uuid}/balance");
        bal1!.Data.Should().Be(400);

        // withdraw 500 -> Conflict (insufficient)
        var w2 = new WithdrawRequest
        {
            Uuid = uuid,
            Player = "alex",
            Amount = 500,
            PluginName = "test",
            Note = "w2",
            DisplayNote = "出金2",
            Server = "dev"
        };
        var res2 = await client.PostAsJsonAsync("/api/bank/withdraw", w2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var bal2 = await client.GetFromJsonAsync<ApiResult<decimal>>($"/api/bank/{uuid}/balance");
        bal2!.Data.Should().Be(400);

        var logs = await client.GetFromJsonAsync<ApiResult<List<MoneyLog>>>($"/api/bank/{uuid}/logs?limit=10");
        logs!.Data!.Count.Should().BeGreaterOrEqualTo(2);
        logs.Data![0].Deposit.Should().BeFalse();
    }
}

