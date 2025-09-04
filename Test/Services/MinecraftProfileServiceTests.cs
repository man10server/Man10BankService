using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Man10BankService.Services;

namespace Test.Services;

public class MinecraftProfileServiceTests
{
    private const string KnownUuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"; // テスト用に自由に変更可
    private const string KnownUuidNormalized = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string KnownName = "TestPlayer"; // テスト用に自由に変更可

    [Fact(DisplayName = "UUIDが存在する場合にMCIDを返す")]
    public async Task GetNameByUuid_ShouldReturnName_WhenExists()
    {
        var http = new HttpClient(new StubHandler(KnownUuidNormalized, KnownName));

        var res = await MinecraftProfileService.GetNameByUuidAsync(KnownUuid, http);

        res.StatusCode.Should().Be(200);
        res.Data.Should().Be(KnownName);
    }

    [Fact(DisplayName = "UUIDが存在しない場合は404を返す")]
    public async Task GetNameByUuid_Should404_WhenNotFound()
    {
        var http = new HttpClient(new StubHandler(KnownUuidNormalized, KnownName));
        var res = await MinecraftProfileService.GetNameByUuidAsync("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", http);

        res.StatusCode.Should().Be(404);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _uuid;
        private readonly string _name;
        public StubHandler(string uuid, string name)
        {
            _uuid = uuid;
            _name = name;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // URL の末尾が UUID 正規化に一致する場合のみヒットさせる
            if (request.RequestUri != null && request.RequestUri.AbsoluteUri.EndsWith(_uuid, StringComparison.Ordinal))
            {
                var json = JsonSerializer.Serialize(new { id = _uuid, name = _name });
                var msg = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(msg);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
