using System.Net.Http.Json;
using FluentAssertions;
using Man10BankService.Models.Requests;
using Man10BankService.Services;
using Xunit;

namespace Man10BankService.Tests;

public class BankApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BankApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Deposit_Then_GetBalance_ShouldReflectChange()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var req = new DepositRequest
        {
            Uuid = "11111111-1111-1111-1111-111111111111",
            Player = "steve",
            Amount = 500,
            PluginName = "test",
            Note = "test deposit",
            DisplayNote = "テスト入金",
            Server = "dev"
        };

        var depositRes = await client.PostAsJsonAsync("/api/bank/deposit", req);
        depositRes.EnsureSuccessStatusCode();
        var depositBody = await depositRes.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        depositBody.Should().NotBeNull();
        depositBody!.StatusCode.Should().Be(200);
        depositBody.Data.Should().Be(500);

        var balanceRes = await client.GetAsync($"/api/bank/{req.Uuid}/balance");
        balanceRes.EnsureSuccessStatusCode();
        var balanceBody = await balanceRes.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        balanceBody.Should().NotBeNull();
        balanceBody!.Data.Should().Be(500);
    }

    [Fact]
    public async Task Withdraw_Insufficient_Should409()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var req = new WithdrawRequest
        {
            Uuid = "22222222-2222-2222-2222-222222222222",
            Player = "alex",
            Amount = 1000,
            PluginName = "test",
            Note = "test withdraw",
            DisplayNote = "テスト出金",
            Server = "dev"
        };

        var res = await client.PostAsJsonAsync("/api/bank/withdraw", req);
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        body!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Logs_ShouldReturnEntries()
    {
        var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var uuid = "33333333-3333-3333-3333-333333333333";
        // 2 deposits
        foreach (var amount in new[] { 100m, 200m })
        {
            var req = new DepositRequest
            {
                Uuid = uuid,
                Player = "alex",
                Amount = amount,
                PluginName = "test",
                Note = "seed",
                DisplayNote = "ログテスト",
                Server = "dev"
            };
            var res = await client.PostAsJsonAsync("/api/bank/deposit", req);
            res.EnsureSuccessStatusCode();
        }

        var logsRes = await client.GetAsync($"/api/bank/{uuid}/logs?limit=10");
        logsRes.EnsureSuccessStatusCode();
        var logsBody = await logsRes.Content.ReadFromJsonAsync<ApiResult<List<object>>>();
        logsBody!.Data!.Count.Should().BeGreaterOrEqualTo(2);
    }
}

