using System;
using Man10BankService.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Test.Infrastructure;

public sealed class TestDbFactory : IDisposable
{
    public required IDbContextFactory<BankDbContext> Factory { get; init; }
    public required ServiceProvider ServiceProvider { get; init; }
    public required SqliteConnection Connection { get; init; }

    public static TestDbFactory Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // SQLite :memory: を共有接続で使用
        var connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        connection.Open();
        services.AddSingleton(connection);
        services.AddPooledDbContextFactory<BankDbContext>(o => o.UseSqlite(connection));

        var sp = services.BuildServiceProvider();

        // スキーマ作成
        using (var scope = sp.CreateScope())
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
            using var db = f.CreateDbContext();
            db.Database.EnsureCreated();
        }

        var factory = sp.GetRequiredService<IDbContextFactory<BankDbContext>>();
        return new TestDbFactory
        {
            Factory = factory,
            ServiceProvider = (ServiceProvider)sp,
            Connection = connection
        };
    }

    public void Dispose()
    {
        try { Connection.Close(); } catch { /* ignore */ }
        try { Connection.Dispose(); } catch { /* ignore */ }
        try { ServiceProvider.Dispose(); } catch { /* ignore */ }
    }
}

