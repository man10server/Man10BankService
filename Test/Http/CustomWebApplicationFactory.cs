using System.Linq;
using Man10BankService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Test.Http;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _conn;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // 既存の IDbContextFactory 登録を除去
            var descriptors = services.Where(d => d.ServiceType == typeof(IDbContextFactory<BankDbContext>)).ToList();
            foreach (var d in descriptors)
                services.Remove(d);

            // SQLite in-memory を共有接続で登録
            _conn = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _conn.Open();
            services.AddSingleton(_conn);
            services.AddDbContextFactory<BankDbContext>(o => o.UseSqlite(_conn));
        });

        var host = base.CreateHost(builder);

        // DB のスキーマを作成
        using var scope = host.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { _conn?.Close(); } catch { /* ignore */ }
        try { _conn?.Dispose(); } catch { /* ignore */ }
    }
}

