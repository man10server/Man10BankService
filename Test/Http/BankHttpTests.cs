using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Man10BankService.Services;
using Man10BankService.Models.Database;
using Test.Infrastructure;

namespace Test.Http;

public class BankHttpTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact(DisplayName = "GET /api/Bank/{uuid}/balance: 200で初期0を返す")]
    public async Task GetBalance_ShouldReturn200_AndZero()
    {
        var client = factory.CreateClient();
        var uuid = "00000000-0000-0000-0000-000000000001";
        var res = await client.GetAsync($"/api/Bank/{uuid}/balance");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        body!.StatusCode.Should().Be(200);
        body.Data.Should().Be(0m);
    }

    [Fact(DisplayName = "GET /openapi/v1.json: Development 環境で 200 を返す")]
    public async Task OpenApi_ShouldReturn200()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/openapi/v1.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await res.Content.ReadAsStringAsync();
        text.Should().Contain("openapi");
    }

    [Fact(DisplayName = "POST /api/Bank/deposit → GET balance/logs: 入金成功で残高とログが更新される")]
    public async Task Deposit_Then_GetBalanceAndLogs_ShouldSucceed()
    {
        var client = factory.CreateClient();
        var uuid = TestConstants.Uuid;

        var depositReq = new
        {
            Uuid = uuid,
            Amount = 500m,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };

        var post = await client.PostAsJsonAsync("/api/Bank/deposit", depositReq);
        post.StatusCode.Should().Be(HttpStatusCode.OK);
        var postBody = await post.Content.ReadFromJsonAsync<ApiResult<decimal>>();
        postBody!.StatusCode.Should().Be(200);
        postBody.Data.Should().Be(500m);

        var balRes = await client.GetAsync($"/api/Bank/{uuid}/balance");
        balRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await balRes.Content.ReadFromJsonAsync<ApiResult<decimal>>())!.Data.Should().Be(500m);

        var logsRes = await client.GetAsync($"/api/Bank/{uuid}/logs?limit=10");
        logsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = (await logsRes.Content.ReadFromJsonAsync<ApiResult<List<MoneyLog>>>())!.Data!;
        logs.Should().NotBeNull();
        logs.Count.Should().Be(1);
        logs[0].Should().BeEquivalentTo(new
        {
            Amount = 500m,
            Deposit = true,
            Uuid = uuid,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        }, options => options.ExcludingMissingMembers());
    }

    [Fact(DisplayName = "POST /api/Bank/deposit: 金額0は400のValidationProblemを返す")]
    public async Task Deposit_InvalidAmount_ShouldReturn400()
    {
        var client = factory.CreateClient();
        var uuid = TestConstants.Uuid;

        var depositReq = new
        {
            Uuid = uuid,
            Amount = 0m,
            PluginName = "test",
            Note = "deposit",
            DisplayNote = "入金テスト",
            Server = "dev"
        };

        var post = await client.PostAsJsonAsync("/api/Bank/deposit", depositReq);
        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await post.Content.ReadAsStringAsync();
        text.Should().Contain("金額は 0 より大きい");

        // 残高とログは未変更
        var balRes = await client.GetAsync($"/api/Bank/{uuid}/balance");
        (await balRes.Content.ReadFromJsonAsync<ApiResult<decimal>>())!.Data.Should().Be(0m);
        var logsRes = await client.GetAsync($"/api/Bank/{uuid}/logs?limit=10");
        (await logsRes.Content.ReadFromJsonAsync<ApiResult<List<MoneyLog>>>())!.Data!.Count.Should().Be(0);
    }
}
