using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>
/// 通过实际 ASP.NET Core 授权管线验收人工核查操作权限与参数传递。
/// </summary>
public sealed class AdminAiManualReviewEndpointTests
{
    private const string Route = "/api/admin/ai/stalled-image-tasks/work-1";

    /// <summary>未认证请求无法执行人工失败处理或触碰数据库。</summary>
    [Fact]
    public async Task Fail_WithoutAdminToken_ReturnsUnauthorized()
    {
        using var app = new TestApiFactory();
        using var client = app.CreateClient();

        using var result = await client.PostAsJsonAsync(Route + "/fail",
            new { generation_id = "job-1", reason = "核对失败" });

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(0, app.Repository.Calls);
    }

    /// <summary>正确管理员认证后，确认失败传入凭据标识和审计原因。</summary>
    [Fact]
    public async Task Fail_WithAdminToken_RecordsReasonAndActor()
    {
        using var app = new TestApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/fail")
        {
            Content = JsonContent.Create(new { generation_id = "job-1", reason = "已核对服务商无结果" })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-key");

        using var result = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("job-1", app.Repository.GenerationId);
        Assert.Equal("admin-token", app.Repository.Actor);
        Assert.Equal("已核对服务商无结果", app.Repository.Reason);
    }

    /// <summary>缺少核实依据时返回 400，不落审计。</summary>
    [Fact]
    public async Task Fail_EmptyReason_ReturnsBadRequest()
    {
        using var app = new TestApiFactory();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/fail")
        {
            Content = JsonContent.Create(new { generation_id = "job-1", reason = "" })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-key");

        using var result = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, app.Repository.Calls);
    }

    /// <summary>补录图片必须使用管理员授权和 multipart 图片文件，不调用 Seedream。</summary>
    [Fact]
    public async Task Complete_WithAdminToken_SavesFileAndResolvesTask()
    {
        using var app = new TestApiFactory();
        using var client = app.CreateClient();
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("job-1"), "generation_id");
        multipart.Add(new StringContent("模型后台已核实成功"), "reason");
        multipart.Add(new ByteArrayContent([137,80,78,71,13,10,26,10,1,2,3,4]), "file", "recovered.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/complete") { Content = multipart };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-admin-key");

        using var result = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, app.Store.SaveCalls);
        Assert.Equal("saved.png", app.Repository.FileName);
    }

    /// <summary>未认证请求不能上传或关联媒体文件。</summary>
    [Fact]
    public async Task Complete_Unauthorized_DoesNotSave()
    {
        using var app = new TestApiFactory();
        using var client = app.CreateClient();
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("job-1"), "generation_id");
        multipart.Add(new StringContent("已核实"), "reason");
        multipart.Add(new ByteArrayContent([1,2,3]), "file", "image.png");

        using var result = await client.PostAsync(Route + "/complete", multipart);

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(0, app.Store.SaveCalls);
        Assert.Equal(0, app.Repository.Calls);
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        public RecordingReviewRepository Repository { get; } = new();
        public RecordingImageStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-key-with-adequate-entropy",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DB_DRIVER"] = "postgres",
                ["ADMIN_TOKEN"] = "test-admin-key"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Tongyin.Application.Admin.IAdminSessionVerifier>();
                services.AddScoped<Tongyin.Application.Admin.IAdminSessionVerifier>(_ => new Tongyin.Api.Tests.Auth.TestAdminSessionVerifier("test-admin-key"));
                services.RemoveAll<IAdminImageReviewRepository>();
                services.AddSingleton<IAdminImageReviewRepository>(Repository);
                services.RemoveAll<IOriginalImageStore>();
                services.AddSingleton<IOriginalImageStore>(Store);
            });
        }
    }

    private sealed class RecordingImageStore : IOriginalImageStore
    {
        public int SaveCalls { get; private set; }

        public Task<string> SaveImageAsync(Stream image, long length, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult("saved.png");
        }

        public void Delete(string fileName) { }
    }

    private sealed class RecordingReviewRepository : IAdminImageReviewRepository
    {
        public int Calls { get; private set; }
        public string? GenerationId { get; private set; }
        public string? Actor { get; private set; }
        public string? Reason { get; private set; }
        public string? FileName { get; private set; }

        public Task<WorkSnapshot> ResolveAsync(string workId, string generationId, string actor, string reason, string? fileName, DateTimeOffset reviewedAt, CancellationToken cancellationToken = default)
        {
            Calls++; GenerationId = generationId; Actor = actor; Reason = reason; FileName = fileName;
            return Task.FromResult(new WorkSnapshot { Id = workId, GenerationId = generationId, Status = fileName is null ? "failed" : "image_ready" });
        }
    }
}
