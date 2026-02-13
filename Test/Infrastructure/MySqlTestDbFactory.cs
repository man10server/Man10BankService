using Man10BankService.Data;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Testcontainers.MySql;

namespace Test.Infrastructure;

public sealed class MySqlTestDbFactory : IDisposable
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static MySqlContainer? _container;

    public required IDbContextFactory<BankDbContext> Factory { get; init; }
    public required ServiceProvider ServiceProvider { get; init; }

    public static MySqlTestDbFactory Create()
    {
        EnsureContainerStarted();
        var connectionString = _container!.GetConnectionString();
        ResetDatabase(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPooledDbContextFactory<BankDbContext>(o =>
            o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
            using var db = f.CreateDbContext();
            ApplySchema(db);
        }

        return new MySqlTestDbFactory
        {
            Factory = sp.GetRequiredService<IDbContextFactory<BankDbContext>>(),
            ServiceProvider = (ServiceProvider)sp
        };
    }

    private static void EnsureContainerStarted()
    {
        if (_container is { State: TestcontainersStates.Running })
            return;

        InitLock.Wait();
        try
        {
            if (_container is { State: TestcontainersStates.Running })
                return;

            _container ??= new MySqlBuilder()
                .WithImage("mysql:8.4")
                .WithDatabase("man10bankservice_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            _container.StartAsync().GetAwaiter().GetResult();
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static void ResetDatabase(string connectionString)
    {
        using var conn = new MySqlConnection(connectionString);
        conn.Open();

        using (var disableFk = conn.CreateCommand())
        {
            disableFk.CommandText = "SET FOREIGN_KEY_CHECKS = 0;";
            disableFk.ExecuteNonQuery();
        }

        var tableNames = new List<string>();
        using (var list = conn.CreateCommand())
        {
            list.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        foreach (var tableName in tableNames)
        {
            using var drop = conn.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS `{tableName.Replace("`", "``")}`;";
            drop.ExecuteNonQuery();
        }

        using (var enableFk = conn.CreateCommand())
        {
            enableFk.CommandText = "SET FOREIGN_KEY_CHECKS = 1;";
            enableFk.ExecuteNonQuery();
        }
    }

    private static void ApplySchema(BankDbContext db)
    {
        var schemaPath = ResolveSchemaPath();
        var sql = string.Join(
            '\n',
            File.ReadLines(schemaPath).Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        var statements = sql
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x));

        foreach (var statement in statements)
        {
            db.Database.ExecuteSqlRaw(statement);
        }
    }

    private static string ResolveSchemaPath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../sql/db.sql")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../sql/db.sql"))
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
            throw new FileNotFoundException("sql/db.sql が見つかりません。");
        return path;
    }

    public void Dispose()
    {
        try { ServiceProvider.Dispose(); } catch { }
    }
}
