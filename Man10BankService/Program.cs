var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// DB 接続設定を起動時に一度だけ設定（IConfiguration を渡す）
Man10BankService.Data.BankDbContext.Configure(builder.Configuration);

// DI 登録（プールありの DbContextFactory と BankService）
builder.Services.AddPooledDbContextFactory<Man10BankService.Data.BankDbContext>();
builder.Services.AddSingleton<Man10BankService.Services.BankService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
