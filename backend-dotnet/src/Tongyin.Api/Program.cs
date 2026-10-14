using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Api.Application;
using Tongyin.Api.Adapters;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Persistence;

var settings = Settings.Load();
settings.Validate();
Directory.CreateDirectory(Path.Combine(settings.DataDir, "media"));

// Existing administrator UI assets are linked into the compiled output.
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
Directory.CreateDirectory(webRoot);
// WebRootPath is specified at creation time, not mutated later: TestServer
// validates WebApplicationBuilder.WebHost cannot change the host webroot.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRoot
});
builder.WebHost.UseUrls(settings.ListenUrl());
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IStore>(settings.DBDriver == "file"
    ? new FileStore(Path.Combine(settings.DataDir, "store.json"))
    : new SqlStore(settings.DBDriver, settings.DatabaseUrl));
builder.Services.AddSingleton<CompatAuth>();
builder.Services.AddSingleton<MediaService>();
builder.Services.AddSingleton<AiService>();
builder.Services.AddControllers();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<WorkService>();
builder.Services.AddScoped<WorkUseCases>();
builder.Services.AddSingleton<IWorkRepository,LegacyWorkRepository>();
builder.Services.AddSingleton<IWorkIdsAndTime,GoCompatibleIdsAndTime>();
builder.Services.AddSingleton<IWorkGenerationGateway,AiWorkGenerationGateway>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AdminQueryService>();
builder.Services.AddScoped<AdminCommandService>();
builder.Services.AddScoped<AiManagementService>();
builder.Services.AddScoped<LogisticsService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddHttpClient("wechat", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(190));
builder.Services.AddHttpClient("worker", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("kuaidi", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("wechatpay", client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddHttpClient("download", client => client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();

if (app.Services.GetRequiredService<IStore>() is SqlStore sqlStore)
    await sqlStore.Initialize();
var db = app.Services.GetRequiredService<IStore>();
var repaired = await LegacyDataMigration.Apply(db);
if (repaired.Users > 0 || repaired.Models > 0)
    app.Logger.LogInformation("Legacy Go data repaired: users={Users}, GLBs={Models}", repaired.Users, repaired.Models);
if (!string.IsNullOrEmpty(settings.AdminPassword))
{
    await db.Update(s =>
    {
        if (!s.Admins.ContainsKey(settings.AdminUsername))
            s.Admins[settings.AdminUsername] =
                CompatAuth.NewAdmin(settings.AdminUsername, settings.AdminPassword, "owner");
        return true;
    });
}
var adminsPresent = await db.Read(s => s.Admins.Values.Any(a => !Doc.Bool(a, "disabled")));
if (!adminsPresent && string.IsNullOrEmpty(settings.AdminToken))
    throw new InvalidOperationException("请配置 ADMIN_BOOTSTRAP_PASSWORD 创建第一个后台管理员");

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
// Route admin assets explicitly instead of relying on FileServer's implicit
// default-document rewriting. It also works with WebApplicationFactory, where
// the API webroot may resolve to the *test* assembly rather than the API output.
var adminAssets = AdminAssetServer.Discover(builder.Environment.ContentRootPath);
// A catch-all can match an empty path. Register ONLY this endpoint:
// /admin, /admin/, /admin/index.html and nested assets all use one handler.
// Separate /admin and /admin/ patterns overlap under ASP.NET Core routing.
app.MapGet("/admin/{**asset}", (string? asset) => adminAssets.Serve(asset));

// Controller routing replaces all Minimal API business endpoints.
app.MapControllers();


// Still missing modules are deliberately NOT routed to Go or marked complete.
// Migration acceptance must pass before this application may replace Go.
app.Logger.LogInformation("童印 .NET 迁移验收版（API 路由已迁入，外部服务待验收）监听 {ListenUrl}", settings.ListenUrl());
app.Run();

public partial class Program { }
