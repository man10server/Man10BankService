using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Man10BankService.Models.Requests;
using Test.Infrastructure;

namespace Test.Controllers;

// 認証/認可の検証(DESIGN 1.1 / 4.1)。
// WebApplicationFactory で実HTTPパイプライン(認証・認可ミドルウェア)を通し、
// 401(キーなし)/403(read スコープで POST)/200(admin キー)/Health 匿名 を確認する。
// 共有 MySQL コンテナを使うため MySQL コレクションへ束ねて並列実行を抑止する。
[Collection(MySqlCollection.Name)]
public class AuthenticationTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private const string DepositUuid = "9c4161a9-0f5f-4317-835c-0bb196a7defa";
    private readonly AuthTestWebApplicationFactory _factory;

    public AuthenticationTests(AuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static DepositRequest NewDeposit() => new()
    {
        Uuid = DepositUuid,
        Amount = 100m,
        PluginName = "test",
        Note = "auth-test",
        DisplayNote = "認証テスト",
        Server = "dev"
    };

    [Fact(DisplayName = "認証: キー無しの GET は 401 を返す")]
    public async Task Get_WithoutKey_ShouldReturn401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/api/Bank/{DepositUuid}/balance");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "認証: キー無しの POST は 401 を返す")]
    public async Task Post_WithoutKey_ShouldReturn401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/Bank/deposit", NewDeposit());
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "認証: 無効なキーは 401 を返す")]
    public async Task Get_WithInvalidKey_ShouldReturn401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid-key");
        var res = await client.GetAsync($"/api/Bank/{DepositUuid}/balance");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "認可: read スコープのキーで POST すると 403 を返す")]
    public async Task Post_WithReadScope_ShouldReturn403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestWebApplicationFactory.ReadKey);
        var res = await client.PostAsJsonAsync("/api/Bank/deposit", NewDeposit());
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "認可: read スコープのキーでも GET は 200(認証のみで許可)")]
    public async Task Get_WithReadScope_ShouldReturn200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestWebApplicationFactory.ReadKey);
        var res = await client.GetAsync($"/api/Bank/{DepositUuid}/balance");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "認可: admin キーで GET すると 200 を返す")]
    public async Task Get_WithAdminKey_ShouldReturn200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestWebApplicationFactory.AdminKey);
        var res = await client.GetAsync($"/api/Bank/{DepositUuid}/balance");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "認可: admin キーで POST すると 200 を返す")]
    public async Task Post_WithAdminKey_ShouldReturn200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthTestWebApplicationFactory.AdminKey);

        // 実運用と同じく、ログイン時の名簿登録(POST /api/Players)を済ませてから入金する。
        // 名前解決は名簿(player_profile)→口座の順で行われ、未登録かつ口座無しの UUID への入金は 404 になるため。
        var register = await client.PostAsJsonAsync("/api/Players",
            new PlayerRegisterRequest { Uuid = DepositUuid, Name = "auth-test" });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await client.PostAsJsonAsync("/api/Bank/deposit", NewDeposit());
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "認証: Health はキー無しでも匿名で 200 を返す")]
    public async Task Health_WithoutKey_ShouldReturn200()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/Health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
