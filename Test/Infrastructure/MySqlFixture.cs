using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Man10BankService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;

namespace Test.Infrastructure;

public class MySqlFixture : IAsyncLifetime
{
    public MySqlContainer Container { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Container = new MySqlBuilder()
            .WithImage("mysql:8.3")
            .WithUsername("test")
            .WithPassword("testpwd")
            .WithDatabase("man10bank")
            .Build();

        await Container.StartAsync();
        await Task.Delay(1000);
    }

    public async Task DisposeAsync()
    {
        if (Container != null)
        {
            await Container.StopAsync();
            await Container.DisposeAsync();
        }
    }
}

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly MySqlFixture _fixture;
    public ApiFactory(MySqlFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // 既存の DbContextFactory を除去
            var regs = services.Where(d => d.ServiceType == typeof(IDbContextFactory<BankDbContext>) ||
                                           (d.ServiceType.FullName?.Contains("PooledDbContextFactory") ?? false)).ToList();
            foreach (var r in regs) services.Remove(r);

            // コンテナ接続で DbContextFactory を再登録
            var cs = _fixture.Container.GetConnectionString();
            services.AddPooledDbContextFactory<BankDbContext>(opts =>
                opts.UseMySql(cs, ServerVersion.AutoDetect(cs)));

            // スキーマ初期化
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BankDbContext>>();
            using var db = factory.CreateDbContext();
            ApplySchemaAsync(db).GetAwaiter().GetResult();
        });
    }

    private static async Task ApplySchemaAsync(BankDbContext db)
    {
        var path = FindFile("sql/db.sql");
        var sql = await File.ReadAllTextAsync(path);
        // 素朴に区切って実行
        foreach (var stmt in sql.Split(';'))
        {
            var s = stmt.Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            await db.Database.ExecuteSqlRawAsync(s);
        }
    }

    private static string FindFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var path = Path.Combine(dir, relative);
            if (File.Exists(path)) return path;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException(relative);
    }
}

