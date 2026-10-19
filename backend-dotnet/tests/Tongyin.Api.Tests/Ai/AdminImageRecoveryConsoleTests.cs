using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>运营人员在 .NET Web 后台对超时图片任务执行人工核查的页面契约。</summary>
public sealed class AdminImageRecoveryConsoleTests
{
    /// <summary>运营后台明确展示超时图片任务入口和人工核查范围，而不把普通失败作品误称为可自动恢复。</summary>
    [Fact]
    public async Task Console_HasStalledImageReviewTabAndScope()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/admin/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-tab=\"review\"", html);
        Assert.Contains("超时图片任务", html);
        Assert.Contains("15 分钟", html);
    }

    /// <summary>页面获取真实超时任务并携带当前任务编号与核实原因调用已存在的人工失败审核接口。</summary>
    [Fact]
    public async Task Console_UsesVerifiedTaskFailureEndpoint()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        var js = await client.GetStringAsync("/admin/orders.js");
        Assert.Contains("/api/admin/ai/stalled-image-tasks", js);
        Assert.Contains("/fail", js);
        Assert.Contains("generation_id", js);
        Assert.Contains("reason", js);
        Assert.Contains("window.confirm", js);
    }

    /// <summary>人工补录通过已存在的 multipart 上传接口传送真实图片，不能把未生成图片标记为成功。</summary>
    [Fact]
    public async Task Console_UploadsActualImageWithGenerationIdAndReason()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        var js = await client.GetStringAsync("/admin/orders.js");
        Assert.Contains("/complete", js);
        Assert.Contains("new FormData(", js);
        Assert.Contains("form.append('generation_id'", js);
        Assert.Contains("form.append('reason'", js);
        Assert.Contains("form.append('file'", js);
        Assert.Contains("image/png,image/jpeg,image/webp", js);
    }

    private sealed class TestApp : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "admin-review-console-secret-is-long-enough",
                ["DB_DRIVER"] = "postgres",
                ["DB_CONNECTION_STRING"] = "unused-in-static-console-test",

            }));
        }
    }
}
