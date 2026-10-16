using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.Controllers;
using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>
/// 核查接口只能让配置有管理员令牌的请求查询跨用户图片任务。
/// </summary>
public sealed class AdminAiTasksControllerTests
{
    /// <summary>
    /// 不带管理员令牌时，不应查询数据库。
    /// </summary>
    [Fact]
    public async Task GetStalledAsync_WithoutToken_RejectsBeforeQuery()
    {
        var reader = new RecordingReader();
        var controller = CreateController(reader, "configured-admin-secret");

        var response = await controller.GetStalledAsync(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Equal(0, reader.Calls);
    }

    /// <summary>
    /// 令牌错误时不能返回跨用户的任务数据。
    /// </summary>
    [Fact]
    public async Task GetStalledAsync_WrongToken_RejectsBeforeQuery()
    {
        var reader = new RecordingReader();
        var controller = CreateController(reader, "configured-admin-secret");
        controller.Request.Headers["X-Admin-Token"] = "wrong-token";

        var response = await controller.GetStalledAsync(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Equal(0, reader.Calls);
    }

    /// <summary>
    /// 生产环境没有配置 ADMIN_TOKEN 时接口默认关闭。
    /// </summary>
    [Fact]
    public async Task GetStalledAsync_NoConfiguredAdminToken_RejectsEvenProvidedHeader()
    {
        var reader = new RecordingReader();
        var controller = CreateController(reader, "");
        controller.Request.Headers["X-Admin-Token"] = "anything";

        var response = await controller.GetStalledAsync(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
        Assert.Equal(0, reader.Calls);
    }

    /// <summary>
    /// 有效管理员令牌返回只读核查清单，固定阈值为至少 15 分钟、最多 100 条。
    /// </summary>
    [Fact]
    public async Task GetStalledAsync_ValidToken_ReturnsReviewTasks()
    {
        var reader = new RecordingReader();
        var controller = CreateController(reader, "configured-admin-secret");
        controller.Request.Headers["X-Admin-Token"] = "configured-admin-secret";
        var before = DateTimeOffset.UtcNow;

        var response = await controller.GetStalledAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var task = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<StalledImageTask>>(ok.Value));
        Assert.Equal("work-001", task.WorkId);
        Assert.Equal("job-001", task.GenerationId);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(100, reader.LastLimit);
        Assert.InRange(reader.LastCutoff, before.AddMinutes(-16), before.AddMinutes(-14));
    }

    private static AdminAiTasksController CreateController(RecordingReader reader, string? adminToken)
    {
        return new AdminAiTasksController(new ListStalledImageTasks(reader), new AdminTokenValidator(adminToken))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class RecordingReader : IStalledImageTasksReader
    {
        public int Calls { get; private set; }
        public int LastLimit { get; private set; }
        public DateTimeOffset LastCutoff { get; private set; }

        public Task<IReadOnlyList<StalledImageTask>> ListAsync(DateTimeOffset startedBefore, int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCutoff = startedBefore;
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<StalledImageTask>>(
                [new StalledImageTask
                {
                    WorkId = "work-001", GenerationId = "job-001", StartedAt = startedBefore.AddHours(-1),
                    ImageAttempts = 1, ProviderId = "seedream-01", IsLegacyGo = false
                }]);
        }
    }
}
