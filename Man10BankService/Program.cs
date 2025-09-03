var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// DB 接続設定を起動時に一度だけ設定（IConfiguration を渡す）
Man10BankService.Data.BankDbContext.Configure(builder.Configuration);

// DI 登録
builder.Services.AddPooledDbContextFactory<Man10BankService.Data.BankDbContext>(_ => {  });
builder.Services.AddSingleton<Man10BankService.Services.BankService>();
builder.Services.AddSingleton<Man10BankService.Services.AtmService>();
builder.Services.AddSingleton<Man10BankService.Services.ChequeService>();
builder.Services.AddSingleton<Man10BankService.Services.ServerLoanService>();
builder.Services.AddSingleton<Man10BankService.Services.LoanService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

// サーバーローンの定期タスクを起動
app.Services.GetRequiredService<Man10BankService.Services.ServerLoanService>().StartScheduler();

app.Run();
