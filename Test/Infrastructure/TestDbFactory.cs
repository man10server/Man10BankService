using System;
using System.IO;
using Man10BankService.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Test.Infrastructure;

public sealed class TestDbFactory : IDisposable
{
    public required IDbContextFactory<BankDbContext> Factory { get; init; }
    public required ServiceProvider ServiceProvider { get; init; }
    public required string ConnectionString { get; init; }
    public required string DatabasePath { get; init; }

    public static TestDbFactory Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // テストごとに一意なファイルDB(WAL モード)を使い、DbContext ごとに個別の接続を張る。
        // 以前の「共有キャッシュ :memory: + 単一 SqliteConnection 共有」は、全 DB アクセスが
        // BankService のワーカーで直列化される前提でしか成立せず、ワーカー外の読み取り
        // (名前解決・残高参照など)が書き込みトランザクションと並行すると接続競合で失敗する。
        // WAL のファイルDBなら読み取りと書き込みが並行でき、本番 MySQL(MVCC)に近い挙動で検証できる。
        var path = Path.Combine(Path.GetTempPath(), $"man10bank_test_{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();
        services.AddPooledDbContextFactory<BankDbContext>(o => o.UseSqlite(connectionString));

        var sp = services.BuildServiceProvider();

        // スキーマ作成 + WAL 有効化(ジャーナルモードはDBファイルに永続化されるため1回でよい)
        using (var scope = sp.CreateScope())
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
            using var db = f.CreateDbContext();
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }

        var factory = sp.GetRequiredService<IDbContextFactory<BankDbContext>>();
        return new TestDbFactory
        {
            Factory = factory,
            ServiceProvider = (ServiceProvider)sp,
            ConnectionString = connectionString,
            DatabasePath = path
        };
    }

    // DB 停止を模擬する。プールされた接続を閉じ、DBファイルを同名ディレクトリに置き換えて
    // 以後の接続オープンを失敗(SQLITE_CANTOPEN)させる。
    public void SimulateDbDown()
    {
        ClearPool();
        DeleteDatabaseFiles();
        Directory.CreateDirectory(DatabasePath);
    }

    private void ClearPool()
    {
        using var conn = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(conn);
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(DatabasePath + suffix); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        try { ServiceProvider.Dispose(); } catch { /* ignore */ }
        try { ClearPool(); } catch { /* ignore */ }
        DeleteDatabaseFiles();
        try { if (Directory.Exists(DatabasePath)) Directory.Delete(DatabasePath); } catch { /* ignore */ }
    }
}
