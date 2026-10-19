using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Ai;
using Tongyin.Infrastructure.Ai;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>管理员图片与模型配置的 HTTP/业务数据库验收，防止泄漏 API Key 或破坏旧配置。</summary>
public sealed class AdminAiSettingsEndpointTests
{
    /// <summary>未登录的用户不能查询或保存服务商和密钥。</summary>
    [Fact]
    public async Task Settings_RequireAdminAuthorization()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        using var get = await client.GetAsync("/api/admin/ai/settings/image");
        using var put = await client.PutAsJsonAsync("/api/admin/ai/settings/model",
            new { endpoint = "", model = "", enabled = false, api_key = "", prompt_template = "test" });

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(0, factory.CountProviders());
    }

    /// <summary>历史提示词、默认配置和密钥状态可读，但密文及明文绝不能通过 GET 返回。</summary>
    [Fact]
    public async Task ImageSettings_SaveAndRead_ProtectsApiKeyAndEnablesDispatch()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        var key = "sk-test-provider-private-key";
        using var save = await client.SendAsync(AdminPut("image", new
        {
            endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
            model = "doubao-seedream-4-0", enabled = true, api_key = key,
            prompt_template = "根据儿童画制作一个适合打印的卡通角色"
        }));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var get = await client.SendAsync(AdminGet("image"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var json = await get.Content.ReadAsStringAsync();
        Assert.Contains("doubao-seedream-4-0", json);
        Assert.Contains("hasApiKey", json);
        Assert.DoesNotContain(key, json);
        Assert.DoesNotContain("apiKeyCipher", json);
        Assert.DoesNotContain("api_key_cipher", json);
        var payload = factory.GetProviderPayload("image");
        Assert.DoesNotContain(key, payload);
        using var scope = factory.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IAiGenerationSettingsReader>().GetAsync("image");
        Assert.True(settings.CanGenerateImage());
        Assert.Equal(key, scope.ServiceProvider.GetRequiredService<AiCredentialProtector>().Decrypt(settings.Provider!.ApiKeyCipher));
        Assert.DoesNotContain(key, factory.GetAuditPayload());
    }

    /// <summary>留空 API Key 不会覆盖已有密文；提示词独立升版且生成派发立即读取新值。</summary>
    [Fact]
    public async Task ImageSettings_UpdatePromptWithoutKey_RetainsEncryptedCredential()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(AdminPut("image", Image("private-key", "旧提示词")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var cipher = factory.GetProviderPayload("image");
        using var changed = await client.SendAsync(AdminPut("image", Image("", "新的提示词")));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(cipher, factory.GetProviderPayload("image"));
        using var scope = factory.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IAiGenerationSettingsReader>().GetAsync("image");
        Assert.Equal("新的提示词", settings.PromptTemplate);
        Assert.Equal(2, settings.PromptVersion);
        Assert.True(settings.CanGenerateImage());
    }

    /// <summary>混元 3D 的 HTTPS 公网协议配置应写入实际 Worker 读取的表。</summary>
    [Fact]
    public async Task ModelSettings_SaveHunyuanAndPrompt_CanDispatchModel()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        using var saved = await client.SendAsync(AdminPut("model", new
        {
            endpoint = "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit",
            model = "3.0", enabled = true, api_key = "hunyuan-private-key",
            prompt_template = "保留五官，生成可打印 GLB"
        }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var scope = factory.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IAiGenerationSettingsReader>().GetAsync("model");
        Assert.True(settings.CanGenerateModel());
        Assert.Equal("保留五官，生成可打印 GLB", settings.PromptTemplate);
        using var read = await client.SendAsync(AdminGet("model"));
        var response = await read.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunyuan-private-key", response);
    }

    /// <summary>无效主机、缺失 Key 以及不支持的模型阶段不能被启用。</summary>
    [Fact]
    public async Task Settings_RejectUnsafeEndpointAndMissingCredential()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        using var unsafeWorker = await client.SendAsync(AdminPut("model", new
        {
            endpoint = "http://localhost:18080/api/worker", model = "3d",
            enabled = true, api_key = "sk-test", prompt_template = "prompt"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, unsafeWorker.StatusCode);
        using var noKey = await client.SendAsync(AdminPut("image", Image("", "prompt")));
        Assert.Equal(HttpStatusCode.Conflict, noKey.StatusCode);
        using var invalidStage = await client.SendAsync(AdminGet("other"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidStage.StatusCode);
        Assert.Equal(0, factory.CountProviders());
    }

    /// <summary>禁用默认服务商后禁止认领新图任务，但保留已保存的 Key 和历史数据。</summary>
    [Fact]
    public async Task ImageSettings_DisablingStopsDispatchWithoutErasingKey()
    {
        using var factory = new SettingsFactory();
        using var client = factory.CreateClient();
        using var first = await client.SendAsync(AdminPut("image", Image("kept-secret", "prompt")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var disabled = await client.SendAsync(AdminPut("image", new
        {
            endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
            model = "doubao-seedream-4-0", enabled = false, api_key = "", prompt_template = "prompt"
        }));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using var scope = factory.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IAiGenerationSettingsReader>().GetAsync("image");
        Assert.False(settings.CanGenerateImage());
        using var status = await client.SendAsync(AdminGet("image"));
        var json = await status.Content.ReadAsStringAsync();
        Assert.Contains("\"hasApiKey\":true", json);
        Assert.Contains("\"enabled\":false", json);
    }

    private static object Image(string key, string prompt) => new
    {
        endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
        model = "doubao-seedream-4-0", enabled = true, api_key = key, prompt_template = prompt
    };

    private static HttpRequestMessage AdminGet(string stage) => Admin(HttpMethod.Get, stage);

    private static HttpRequestMessage AdminPut(string stage, object body)
    {
        var request = Admin(HttpMethod.Put, stage);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Admin(HttpMethod method, string stage)
    {
        var request = new HttpRequestMessage(method, "/api/admin/ai/settings/" + stage);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-admin-key");
        return request;
    }

    private sealed class SettingsFactory : WebApplicationFactory<Program>
    {
        public const string Secret = "admin-ai-settings-secret-at-least-32-bytes";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tongyin-ai-settings-" + Guid.NewGuid().ToString("N"));
        private string DbFile => Path.Combine(_root, "settings.db");

        public SettingsFactory()
        {
            Directory.CreateDirectory(_root);
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES(1);
                CREATE TABLE ty_ai_providers(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_ai_prompts(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_audit(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                """);
        }

        private DbConnection Connect() => new SqliteConnection($"Data Source={DbFile};Pooling=False");

        public int CountProviders()
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ty_ai_providers");
        }

        public string GetProviderPayload(string stage)
        {
            using var db = Connect();
            db.Open();
            var payloads = db.Query<string>("SELECT payload FROM ty_ai_providers").ToArray();
            return Assert.Single(payloads.Where(x => x.Contains("\"stage\":\"" + stage + "\"", StringComparison.Ordinal)));
        }

        public string GetAuditPayload()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_audit ORDER BY created_at LIMIT 1");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = Secret,
                ["DB_CONNECTION_STRING"] = "test-only",
                ["DB_DRIVER"] = "postgres",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "60",
                ["AI_MODEL_DISPATCH_POLL_SECONDS"] = "60",
                ["DATA_DIR"] = _root
            }));
            builder.ConfigureTestServices(services =>
            {
                Tongyin.Api.Tests.Auth.TestAdminAuthExtensions.AddTestAdminAuthentication(services, "test-admin-key");
                services.RemoveAll<Func<DbConnection>>();
                services.AddSingleton<Func<DbConnection>>(_ => Connect);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
