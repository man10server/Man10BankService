using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// DB 接続設定を起動時に一度だけ設定（IConfiguration を渡す）
Man10BankService.Data.BankDbContext.Configure(builder.Configuration);

// DI 登録（プーリング有効）: DbContext のオプションは DI 側で構成
var cs = Man10BankService.Data.BankDbContext.GetConnectionString();
builder.Services.AddPooledDbContextFactory<Man10BankService.Data.BankDbContext>(o =>
    o.UseMySql(cs, ServerVersion.AutoDetect(cs))
);
builder.Services.AddSingleton<Man10BankService.Services.IPlayerProfileService, Man10BankService.Services.MojangPlayerProfileService>();
builder.Services.AddSingleton<Man10BankService.Services.BankService>();
builder.Services.AddSingleton<Man10BankService.Services.AtmService>();
builder.Services.AddSingleton<Man10BankService.Services.ChequeService>();
builder.Services.AddSingleton<Man10BankService.Services.ServerLoanService>();
builder.Services.AddSingleton<Man10BankService.Services.LoanService>();
builder.Services.AddSingleton<Man10BankService.Services.EstateService>();
builder.Services.AddSingleton<Man10BankService.Services.ServerEstateService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Man10BankService.Data.BankDbContext>>();
    using var db = dbFactory.CreateDbContext();
    var provider = db.Database.ProviderName;
    if (provider is null || !provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("このアプリケーションはMySQLプロバイダでのみ実行できます。");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI は組み込み OpenAPI (/openapi/v1.json) を表示
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Man10BankService v1");
        o.RoutePrefix = "swagger"; // /swagger で提供
        o.DocumentTitle = "Man10BankService API";
    });
}

// コンテナ（HTTP のみ想定）では HTTPS リダイレクトを行わない
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();

// WebApplicationFactory 用のエントリーポイント
public partial class Program { }
