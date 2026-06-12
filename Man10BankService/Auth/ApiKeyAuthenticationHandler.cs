using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Man10BankService.Auth;

// Authorization: Bearer <APIキー> を検証する認証ハンドラ
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string ScopeClaimType = "scope";

    private readonly ApiKeyAuthSettings _settings;
    private readonly IHostEnvironment _environment;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyAuthSettings> settings,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _settings = settings.Value;
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 空文字/空白のみのキーは未設定として扱う(appsettings.json の雛形エントリが
        // Key:"" のまま残っていても「キー未設定」と判定を揃える。Program.cs の本番チェックと同条件)
        var keys = _settings.ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k.Key)).ToList();

        // キー未設定時の環境別挙動
        if (keys.Count == 0)
        {
            // 開発環境ではキー未設定でも匿名で許可（管理スコープ付与）
            if (_environment.IsDevelopment())
            {
                Logger.LogWarning("APIキーが未設定のため、開発環境として匿名アクセスを許可します。");
                return Task.FromResult(AuthenticateResult.Success(BuildTicket("development", ["admin"])));
            }

            // 本番環境では起動時に弾く想定だが、保険として認証失敗を返す
            return Task.FromResult(AuthenticateResult.Fail("APIキーが未設定です。"));
        }

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var raw = authHeader.ToString();
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var presented = raw[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        // 比較時間からキーを推測されないよう固定時間比較を使う
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var matched = keys.FirstOrDefault(k =>
        {
            var keyBytes = Encoding.UTF8.GetBytes(k.Key);
            return keyBytes.Length == presentedBytes.Length
                   && CryptographicOperations.FixedTimeEquals(keyBytes, presentedBytes);
        });
        if (matched == null)
            return Task.FromResult(AuthenticateResult.Fail("APIキーが無効です。"));

        return Task.FromResult(AuthenticateResult.Success(BuildTicket(matched.Name, matched.Scopes)));
    }

    private AuthenticationTicket BuildTicket(string name, IEnumerable<string> scopes)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        claims.AddRange(scopes.Select(s => new Claim(ScopeClaimType, s)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }
}
