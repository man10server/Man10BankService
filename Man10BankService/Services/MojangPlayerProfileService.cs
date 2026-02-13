using System.Text.Json;
using System.Text.RegularExpressions;

namespace Man10BankService.Services;

public partial class MojangPlayerProfileService : IPlayerProfileService
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

    private sealed class MojangProfile
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ja-JP")]
    private static partial Regex MyRegex();

    public async Task<string?> GetNameByUuidAsync(string uuid, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid))
                return null;

            var normalized = uuid.Replace("-", string.Empty).Trim().ToLowerInvariant();
            if (!UuidHex32.IsMatch(normalized))
                return null;

            var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{normalized}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);

            if (res.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
                return null;

            if (!res.IsSuccessStatusCode)
                return null;

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var profile = await JsonSerializer.DeserializeAsync<MojangProfile>(stream, JsonOpts, ct);

            var name = profile?.Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
