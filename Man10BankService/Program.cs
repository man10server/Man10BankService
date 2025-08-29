var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// DB 接続設定を起動時に一度だけ設定（Database セクションから組み立て）
var dbSection = builder.Configuration.GetSection("Database");
var host = dbSection["Host"] ?? "localhost";
var port = dbSection["Port"] ?? "3306";
var name = dbSection["Name"] ?? "man10bank";
var user = dbSection["User"] ?? "root";
var password = dbSection["Password"] ?? "";
var treatTiny = (dbSection["TreatTinyAsBoolean"] ?? "true").ToLowerInvariant();
var conn = $"Server={host};Port={port};Database={name};User Id={user};Password={password};TreatTinyAsBoolean={treatTiny};";
Man10BankService.Data.BankDbContext.ConfigureConnection(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
