using System.Text.Json.Nodes;
using Tongyin.Api.Core;
using Tongyin.Api.Api;

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
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/admin", () => Results.Redirect("/admin/"));
app.MapGet("/health", () => Results.Json(new { status = "ok", backend = "dotnet" }));
app.MapGet("/api/config", async (IStore store) => Results.Json(new
{
    price_fen = settings.PriceFen,
    dev = settings.DevMode,
    ai_auto_enabled = await store.Read(s => s.AIProviders.Values.Any(p => Doc.Bool(p,"default") && Doc.Bool(p,"enabled") && Doc.Str(p,"stage")=="image")) || settings.AiWorkerUrl.Length > 0,
    free_image_attempts = settings.FreeImageAttempts
}));
app.MapCustomers();
app.MapWorks();
app.MapOrders();
app.MapAdminFoundation();
app.MapAi();
app.MapAdminOperations();
app.MapLogistics();
app.MapPayment();

// Still missing modules are deliberately NOT routed to Go or marked complete.
// Migration acceptance must pass before this application may replace Go.
app.Logger.LogInformation("童印 .NET 迁移服务（未完成全部 Go API）监听 {ListenUrl}", settings.ListenUrl());
app.Run();

public partial class Program { }
