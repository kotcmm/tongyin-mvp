using System.Data.Common;
using MySqlConnector;
using Npgsql;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Auth;
using Tongyin.Infrastructure.Works;
using Tongyin.Api.Works;
using Tongyin.Infrastructure.Media;

var builder = WebApplication.CreateBuilder(args);

var sessionSecret = builder.Configuration["SESSION_SECRET"];
if (string.IsNullOrEmpty(sessionSecret) || sessionSecret.Length < 24)
{
    throw new InvalidOperationException("SESSION_SECRET 至少需要 24 位，不能使用默认密钥");
}

var connectionString = builder.Configuration["DB_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DB_CONNECTION_STRING 未配置（使用 Npgsql / MySqlConnector 格式）");
}

var driver = builder.Configuration["DB_DRIVER"] ?? "postgres";
builder.Services.AddSingleton<Func<DbConnection>>(_ => driver switch
{
    "postgres" => () => new NpgsqlConnection(connectionString),
    "mysql" => () => new MySqlConnection(connectionString),
    _ => throw new InvalidOperationException("仅支持 postgres 或 mysql 数据库驱动")
});
builder.Services.AddSingleton(new GoSessionTokenValidator(sessionSecret));
builder.Services.AddSingleton(new LocalMediaStore(
    builder.Configuration["DATA_DIR"] ?? "./data",
    builder.Configuration["PUBLIC_URL"] ?? "http://localhost:8080",
    sessionSecret));
builder.Services.AddScoped<IWorkRepository, SqlWorkRepository>();
builder.Services.AddScoped<SqlUserAccess>();
builder.Services.AddScoped<CreateWork>();

var app = builder.Build();
WorkEndpoints.Map(app);
app.Run();

/// <summary>
/// .NET 童印 HTTP 入口，用于集成测试和生产环境宿主。
/// </summary>
public partial class Program
{
}
