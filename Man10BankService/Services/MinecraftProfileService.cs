using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Man10BankService.Services;

public static partial class MinecraftProfileService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static readonly Regex UuidHex32 = MyRegex();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Java版の UUID (ハイフンあり/なし可) から現在の MCID を取得します。
    /// </summary>
    /// <param name="uuid">プレイヤーUUID（32桁のhex。ハイフンありでも可）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>成功時: 200 + MCID / 見つからない: 404 / その他: 5xx</returns>
    public static async Task<ApiResult<string>> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return ApiResult<string>.BadRequest("UUID を指定してください。");

            var normalized = uuid.Replace("-", string.Empty).Trim().ToLowerInvariant();
            if (!UuidHex32.IsMatch(normalized))
                return ApiResult<string>.BadRequest("UUID の形式が不正です。(32桁hex)");

            var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{normalized}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);

            if (res.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
                return ApiResult<string>.NotFound("プレイヤーが見つかりませんでした。");

            if (!res.IsSuccessStatusCode)
                return ApiResult<string>.Error($"プロフィール取得に失敗しました: {(int)res.StatusCode} {res.ReasonPhrase}");

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var profile = await JsonSerializer.DeserializeAsync<MojangProfile>(stream, JsonOpts, ct);

            var name = profile?.Name;
            if (string.IsNullOrWhiteSpace(name))
                return ApiResult<string>.Error("レスポンスに name が含まれていません。");

            return ApiResult<string>.Ok(name);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<string>.Error("プロフィール取得がタイムアウトしました。");
        }
        catch (Exception ex)
        {
            return ApiResult<string>.Error($"プロフィール取得中にエラー: {ex.Message}");
        }
    }

    

    private sealed class MojangProfile
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ja-JP")]
    private static partial Regex MyRegex();
}
