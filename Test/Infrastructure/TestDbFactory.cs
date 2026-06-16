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

        // テストごとに一意な名前付きインメモリDBを使用。
        // "DataSource=:memory:;Cache=Shared" は同一接続文字列の全接続が1つのDBを共有するため、
        // 並列実行されるテストクラス間でデータが混ざる。一意名にして完全に分離する。
        var connection = new SqliteConnection($"DataSource=test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
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

