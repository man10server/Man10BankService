using Man10BankService.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// ルート制約 {uuid:uuid} に厳密UUID検証を登録
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteOptions>(options =>
{
    options.ConstraintMap["uuid"] = typeof(Man10BankService.Validation.UuidRouteConstraint);
});

// 自動400(モデルバリデーション)にも code:"ValidationError" を付与
// JSON の enum は文字列で受ける(VaultMoveDirection / VaultSource など)。
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest
        };
        problem.Extensions["code"] = "ValidationError";
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

// 未処理例外を ProblemDetails(code:"UnexpectedError") として返す基盤
builder.Services.AddProblemDetails();

// DB 接続文字列を IConfiguration から組み立てる（値は MySqlConnectionStringBuilder でエスケープ）
var dbSection = builder.Configuration.GetSection("Database");
var csBuilder = new MySqlConnectionStringBuilder
{
    Server = dbSection["Host"] ?? "",
    Port = uint.TryParse(dbSection["Port"], out var port) ? port : 3306,
    Database = dbSection["Name"] ?? "",
    UserID = dbSection["User"] ?? "",
    Password = dbSection["Password"] ?? "",
    TreatTinyAsBoolean = !bool.TryParse(dbSection["TreatTinyAsBoolean"], out var treatTiny) || treatTiny
};
var connectionString = csBuilder.ConnectionString;

// DI 登録（プーリング有効）
builder.Services.AddPooledDbContextFactory<Man10BankService.Data.BankDbContext>(o =>
    o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

// 認証(APIキー)設定をバインド
builder.Services.Configure<ApiKeyAuthSettings>(builder.Configuration.GetSection("Auth"));

// Production: APIキー未設定なら起動時に例外で失敗（fail-closed）
if (builder.Environment.IsProduction())
{
    var auth = builder.Configuration.GetSection("Auth").Get<ApiKeyAuthSettings>();
    if (auth is null || auth.ApiKeys.Count == 0 || auth.ApiKeys.All(k => string.IsNullOrWhiteSpace(k.Key)))
        throw new InvalidOperationException("本番環境では Auth:ApiKeys に有効なAPIキーを設定してください。");
}

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    // すべてのエンドポイントで認証必須（[AllowAnonymous] を除く）
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // 書き込み系(POST全部)は admin スコープ必須
    options.AddPolicy("RequireWriteScope", policy =>
        policy.RequireClaim(ApiKeyAuthenticationHandler.ScopeClaimType, "admin"));
});

// CORS: 設定駆動（既定は無効。将来のWebサイト向けにポリシー雛形のみ用意）
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy("Default", policy =>
    {
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddSingleton<Man10BankService.Services.IPlayerProfileService, Man10BankService.Services.MojangPlayerProfileService>();
builder.Services.AddSingleton<Man10BankService.Services.BankService>();
builder.Services.AddSingleton<Man10BankService.Services.AtmService>();
builder.Services.AddSingleton<Man10BankService.Services.ChequeService>();
builder.Services.AddSingleton<Man10BankService.Services.ServerLoanService>();
builder.Services.AddSingleton<Man10BankService.Services.LoanService>();
builder.Services.AddSingleton<Man10BankService.Services.EstateService>();
builder.Services.AddSingleton<Man10BankService.Services.ServerEstateService>();

// 電子マネー(Vault)関連の登録。
// Vault 設定(残高上限・移動緩和)を "Vault" セクションからバインドする。
builder.Services.Configure<Man10BankService.Services.VaultOptions>(builder.Configuration.GetSection("Vault"));
// push 通知器 = WebSocket ハブ(IVaultNotifier の実体)。同一インスタンスを両者へ解決する。
builder.Services.AddSingleton<Man10BankService.Hubs.VaultWsHub>();
builder.Services.AddSingleton<Man10BankService.Services.IVaultNotifier>(sp =>
    sp.GetRequiredService<Man10BankService.Hubs.VaultWsHub>());
builder.Services.AddSingleton<Man10BankService.Services.VaultService>();

// スケジューラ(BackgroundService)を登録。
// 複数インスタンスを並列起動すると各インスタンスでスケジューラが二重実行され、
// 日次利息の多重課金・週次返済の多重引落し・スナップショット重複INSERTが起きうる。
// そのため Scheduler:Enabled で起動可否を切り替える(既定 true: 単一インスタンス運用と互換)。
// 水平スケール時はリーダー1台のみ true とし、他インスタンスは Scheduler__Enabled=false で起動すること。
var schedulerEnabled = builder.Configuration.GetValue("Scheduler:Enabled", true);
if (schedulerEnabled)
{
    builder.Services.AddHostedService<Man10BankService.Services.ServerLoanSchedulerService>();
    builder.Services.AddHostedService<Man10BankService.Services.ServerEstateSchedulerService>();
}

var app = builder.Build();

// 起動したインスタンスがスケジューラ担当かどうかをログに残す(運用時の取り違え防止)。
app.Logger.LogInformation(
    "スケジューラ(サーバーローン日次利息/週次返済・資産スナップショット): {State}",
    schedulerEnabled ? "有効" : "無効");

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Man10BankService.Data.BankDbContext>>();
    using var db = dbFactory.CreateDbContext();
    var provider = db.Database.ProviderName;
    if (provider is null || !provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("このアプリケーションはMySQLプロバイダでのみ実行できます。");
}

// 未処理例外を ProblemDetails(code:"UnexpectedError")へ変換
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "予期しないエラーが発生しました。",
                Extensions = { ["code"] = "UnexpectedError" }
            }
        });
    });
});

if (app.Environment.IsDevelopment())
{
    // Development 限定のため OpenAPI ドキュメントは匿名で取得可能にする（Swagger UI が参照する）
    app.MapOpenApi().AllowAnonymous();
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

// WebSocket(電子マネー push)を有効化する。KeepAliveInterval で ping/pong による生存監視を行う。
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// WebApplicationFactory 用のエントリーポイント
public partial class Program { }
