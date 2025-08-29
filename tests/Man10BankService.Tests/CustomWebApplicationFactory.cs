using System.Linq;
using Man10BankService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Man10BankService.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // 既存の DbContextFactory 登録を削除
            var descriptors = services
                .Where(d => d.ServiceType == typeof(IDbContextFactory<BankDbContext>) ||
                            d.ServiceType.FullName?.Contains("PooledDbContextFactory") == true)
                .ToList();
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            // InMemory provider を使用
            services.AddDbContextFactory<BankDbContext>(options =>
            {
                options.UseInMemoryDatabase("BankServiceTests");
            });
        });
    }
}

