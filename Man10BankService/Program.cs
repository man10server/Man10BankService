var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// DB 接続設定を起動時に一度だけ設定
var conn = builder.Configuration.GetConnectionString("Default")
           ?? "Server=localhost;Port=3306;Database=man10bank;User Id=root;Password=;TreatTinyAsBoolean=true;";
Man10BankService.Data.BankDbContext.ConfigureConnection(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
