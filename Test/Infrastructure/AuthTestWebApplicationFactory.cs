using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;

namespace Test.Infrastructure;

// 認証/認可を実HTTPパイプライン(ミドルウェア)経由で検証するための WebApplicationFactory。
// Program.cs は MySQL プロバイダ前提のため、共有 MySQL(Testcontainers)へ接続する。
// 環境は Production とし、Auth:ApiKeys に admin/read の2鍵を注入する
// (Development だと鍵未設定時に匿名許可されてしまい、401/403 を検証できないため)。
public sealed class AuthTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key-0123456789abcdef0123456789abcdef";
    public const string ReadKey = "test-read-key-0123456789abcdef0123456789abcdef";

    private readonly MySqlConnectionStringBuilder _cs;

    public AuthTestWebApplicationFactory()
    {
        // 共有 MySQL コンテナを起動し、本番スキーマを適用した接続文字列を得る。
        var connectionString = MySqlTestDbFactory.EnsureSchemaAndGetConnectionString();
        _cs = new MySqlConnectionStringBuilder(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        // fail-closed チェック(Production で Auth:ApiKeys 必須)も DB 接続文字列の組み立ても
        // Program.Main の builder 構築中に IConfiguration から評価されるため、
        // 最も早く読み込まれる UseSetting で値を注入する。
        // appsettings.json の Auth:ApiKeys:0:Key は空文字なので admin 鍵で上書きする。
        builder.UseSetting("Auth:ApiKeys:0:Key", AdminKey);
        builder.UseSetting("Auth:ApiKeys:0:Name", "test-admin");
        builder.UseSetting("Auth:ApiKeys:0:Scopes:0", "admin");
        builder.UseSetting("Auth:ApiKeys:1:Key", ReadKey);
        builder.UseSetting("Auth:ApiKeys:1:Name", "test-read");
        builder.UseSetting("Auth:ApiKeys:1:Scopes:0", "read");

        // Program.cs は Database:* から MySqlConnectionStringBuilder で接続文字列を組み立てる。
        // テスト用コンテナの接続情報で Database:* を上書きし、本物の MySQL に接続させる。
        builder.UseSetting("Database:Host", _cs.Server);
        builder.UseSetting("Database:Port", _cs.Port.ToString());
        builder.UseSetting("Database:Name", _cs.Database);
        builder.UseSetting("Database:User", _cs.UserID);
        builder.UseSetting("Database:Password", _cs.Password);
    }
}
