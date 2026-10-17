using System.Data.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Tongyin.Api.Auth;
using Tongyin.Api.Services;
using MySqlConnector;
using Npgsql;
using Tongyin.Application.Ai;
using Tongyin.Application.Auth;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Auth;
using Tongyin.Infrastructure.Media;
using Tongyin.Infrastructure.Ai;
using Tongyin.Infrastructure.Works;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // 旧小程序对错误 Content-Type 预期返回 400，而不是 MVC 推断的 415。
        options.SuppressConsumesConstraintForFormFileParameters = true;
    });

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
    new WechatSessionTokenService(services.GetRequiredService<IConfiguration>()["SESSION_SECRET"]!));
builder.Services.AddSingleton<IWechatSessionIssuer>(services => services.GetRequiredService<WechatSessionTokenService>());
builder.Services.AddSingleton(services =>
    new WechatAppSecretProtector(services.GetRequiredService<IConfiguration>()["SESSION_SECRET"]!));
builder.Services.AddScoped<IWechatAppSettingsStore, SqlWechatAppSettingsStore>();
builder.Services.AddScoped<ManageWechatAppSettings>();
builder.Services.AddScoped<IWechatCodeExchanger>(services =>
{
    // 使用独立 HTTP 客户端，避免默认 HTTP 工厂日志记录包含 AppSecret 的微信 code2Session URL。
    var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    IWechatCodeExchanger actual = new WechatCodeExchanger(httpClient, services.GetRequiredService<IWechatAppSettingsStore>());
    var environment = services.GetRequiredService<IHostEnvironment>();
    var configuration = services.GetRequiredService<IConfiguration>();

    return environment.IsDevelopment() && configuration.GetValue<bool>("DEV_MODE")
        ? new DevelopmentWechatCodeExchanger(actual)
        : actual;
});
builder.Services.AddSingleton(services =>
    new AdminTokenValidator(services.GetRequiredService<IConfiguration>()["ADMIN_TOKEN"]));
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    return new LocalMediaStore(
        configuration["DATA_DIR"] ?? "./data",
        configuration["PUBLIC_URL"] ?? "http://localhost:8080",
        configuration["SESSION_SECRET"]!);
});
builder.Services.AddSingleton<IOriginalImageStore>(services => services.GetRequiredService<LocalMediaStore>());
builder.Services.AddSingleton<IOriginalImageReader>(services => services.GetRequiredService<LocalMediaStore>());
builder.Services.AddScoped<IWorkRepository, SqlWorkRepository>();
builder.Services.AddScoped<IWorkImageSelectionRepository>(services => (SqlWorkRepository)services.GetRequiredService<IWorkRepository>());
builder.Services.AddScoped<IStalledImageTasksReader, SqlStalledImageTasksReader>();
builder.Services.AddScoped<ListStalledImageTasks>();
builder.Services.AddScoped<IAdminImageReviewRepository, SqlAdminImageReviewRepository>();
builder.Services.AddScoped<ReviewStalledImageTask>();
builder.Services.AddScoped<SqlUserAccess>();
builder.Services.AddScoped<IWechatUserRepository>(services => services.GetRequiredService<SqlUserAccess>());
builder.Services.AddScoped<LoginWechat>();
builder.Services.AddScoped<GetWechatProfile>();
builder.Services.AddScoped<CreateWork>();
builder.Services.AddScoped<StartImageGeneration>();
builder.Services.AddScoped<SelectWorkImage>();
builder.Services.AddSingleton<MiniappBusinessSettings>();
builder.Services.AddScoped<ListWorks>();
builder.Services.AddScoped<GetWork>();
builder.Services.AddScoped<IAiGenerationSettingsReader, SqlAiGenerationSettingsReader>();
builder.Services.AddSingleton(services =>
    new GoAiKeyDecryptor(services.GetRequiredService<IConfiguration>()["SESSION_SECRET"]!));
builder.Services.AddHttpClient<SeedreamImageGenerator>(client => client.Timeout = TimeSpan.FromSeconds(190))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // 服务商无需重定向，避免跨域跳转时泄漏授权头。
        AllowAutoRedirect = false
    });
builder.Services.AddScoped<IImageGenerationClient>(services => services.GetRequiredService<SeedreamImageGenerator>());
builder.Services.AddHttpClient<GeneratedImageDownloader>(client => client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // 结果链接不可信：禁止自动重定向、环境代理和 DNS 重绑定到内网。
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = PublicImageNetworkPolicy.ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1)
    });
builder.Services.AddScoped<IGeneratedImageStore>(services => services.GetRequiredService<GeneratedImageDownloader>());
builder.Services.AddScoped<SaveImageGenerationResult>();
builder.Services.AddScoped<ExecuteImageGeneration>();
builder.Services.AddScoped<IQueuedImageGenerationReader, SqlQueuedImageGenerationReader>();
builder.Services.AddScoped<DispatchQueuedImageGeneration>();
// 图片任务是否可派发由数据库中的默认服务商启用状态控制；没有可用服务商时不派发或计费。
builder.Services.AddHostedService<ImageGenerationPollingService>();

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, WechatSessionAuthenticationHandler>(
        WechatSessionAuthenticationHandler.SchemeName, _ => { })
    .AddScheme<AuthenticationSchemeOptions, AdminTokenAuthenticationHandler>(
        AdminTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Wechat, new AuthorizationPolicyBuilder(WechatSessionAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .AddRequirements(new EnabledWechatUserRequirement())
        .Build());
    options.AddPolicy(AuthorizationPolicies.Admin, new AuthorizationPolicyBuilder(AdminTokenAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build());
});
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
