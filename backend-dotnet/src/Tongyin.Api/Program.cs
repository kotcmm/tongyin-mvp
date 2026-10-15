using System.Data.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Tongyin.Api.Auth;
using MySqlConnector;
using Npgsql;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Auth;
using Tongyin.Infrastructure.Media;
using Tongyin.Infrastructure.Works;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

builder.Services.AddSingleton<Func<DbConnection>>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration["DB_CONNECTION_STRING"]!;
    return (configuration["DB_DRIVER"] ?? "postgres") switch
    {
        "postgres" => () => new NpgsqlConnection(connectionString),
        "mysql" => () => new MySqlConnection(connectionString),
        _ => throw new InvalidOperationException("仅支持 postgres 或 mysql 数据库驱动")
    };
});
builder.Services.AddSingleton(services =>
    new GoSessionTokenValidator(services.GetRequiredService<IConfiguration>()["SESSION_SECRET"]!));
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return new LocalMediaStore(
        configuration["DATA_DIR"] ?? "./data",
        configuration["PUBLIC_URL"] ?? "http://localhost:8080",
        configuration["SESSION_SECRET"]!);
});
builder.Services.AddSingleton<IOriginalImageStore>(services => services.GetRequiredService<LocalMediaStore>());
builder.Services.AddScoped<IWorkRepository, SqlWorkRepository>();
builder.Services.AddScoped<SqlUserAccess>();
builder.Services.AddScoped<CreateWork>();
builder.Services.AddScoped<ListWorks>();

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, WechatSessionAuthenticationHandler>(
        WechatSessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthorizationPolicies.Wechat, new AuthorizationPolicyBuilder(WechatSessionAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .AddRequirements(new EnabledWechatUserRequirement())
        .Build()));
builder.Services.AddScoped<IAuthorizationHandler, EnabledWechatUserAuthorizationHandler>();

var app = builder.Build();

// 测试宿主在 Build 阶段注入配置，此时才能验证最终的环境配置。
var sessionSecret = app.Configuration["SESSION_SECRET"];
if (string.IsNullOrEmpty(sessionSecret) || sessionSecret.Length < 24)
{
    throw new InvalidOperationException("SESSION_SECRET 至少需要 24 位，不能使用默认密钥");
}

if (string.IsNullOrWhiteSpace(app.Configuration["DB_CONNECTION_STRING"]))
{
    throw new InvalidOperationException("DB_CONNECTION_STRING 未配置（使用 Npgsql / MySqlConnector 格式）");
}

if (app.Configuration["DB_DRIVER"] is { } configuredDriver
    && configuredDriver != "postgres" && configuredDriver != "mysql")
{
    throw new InvalidOperationException("仅支持 postgres 或 mysql 数据库驱动");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

/// <summary>
/// .NET 童印 HTTP 入口，用于集成测试和生产环境宿主。
/// </summary>
public partial class Program
{
}
