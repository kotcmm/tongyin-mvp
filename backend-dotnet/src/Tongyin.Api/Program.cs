using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
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
app.UseDefaultFiles();
app.UseStaticFiles();

// The Go version serves its administration HTML/JS from backend/webadmin and
// its 3D viewer from admin/. Keep those EXACT assets rather than rebuilding UI.
// In a published deployment they are linked to <output>/wwwroot/admin; while
// running from the repository, fall back to both original source directories.
var adminProviders = new List<IFileProvider>();
var emittedAdmin = Path.Combine(webRoot, "admin");
if (Directory.Exists(emittedAdmin)) adminProviders.Add(new PhysicalFileProvider(emittedAdmin));
var sourceWebadmin = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../../backend/webadmin"));
var sourceLegacyAdmin = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../../admin"));
if (Directory.Exists(sourceWebadmin)) adminProviders.Add(new PhysicalFileProvider(sourceWebadmin));
if (Directory.Exists(sourceLegacyAdmin)) adminProviders.Add(new PhysicalFileProvider(sourceLegacyAdmin));
if (adminProviders.Count > 0)
{
    var adminContentTypes = new FileExtensionContentTypeProvider();
    adminContentTypes.Mappings[".mjs"] = "text/javascript";
    adminContentTypes.Mappings[".js"] = "text/javascript";
    adminContentTypes.Mappings[".glb"] = "model/gltf-binary";
    app.UseFileServer(new FileServerOptions
    {
        RequestPath = "/admin",
        FileProvider = new CompositeFileProvider(adminProviders),
        EnableDefaultFiles = true,
        EnableDirectoryBrowsing = false,
        StaticFileOptions = { ContentTypeProvider = adminContentTypes }
    });
}

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
app.Logger.LogInformation("童印 .NET 迁移验收版（API 路由已迁入，外部服务待验收）监听 {ListenUrl}", settings.ListenUrl());
app.Run();

public partial class Program { }
