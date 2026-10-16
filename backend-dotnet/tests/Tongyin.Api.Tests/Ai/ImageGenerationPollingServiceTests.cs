using System.Data.Common;
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
/// 轮询后台服务必须显式启用；默认禁止自动派发真实付费请求。
/// </summary>
public sealed class ImageGenerationPollingServiceTests
{
    /// <summary>
    /// 未启用 AI_IMAGE_DISPATCH_ENABLED 时，宿主运行也不得扫描数据库。
    /// </summary>
    [Fact]
    public async Task HostedService_DisabledByDefault_DoesNotPoll()
    {
        using var app = new PollingFactory(enabled: false);
        using var client = app.CreateClient();

        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(0, app.Reader.Calls);
    }

    /// <summary>
    /// 显式启用后台轮询后，应按配置的时间间隔发现待生成任务。
    /// </summary>
    [Fact]
    public async Task HostedService_ExplicitlyEnabled_PollsRegisteredReader()
    {
        using var app = new PollingFactory(enabled: true);
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        while (app.Reader.Calls == 0 && !timeout.IsCancellationRequested)
        {
            await Task.Delay(50);
        }

        Assert.True(app.Reader.Calls > 0, "启用后台轮询后应该发现待执行任务");
    }

    private sealed class PollingFactory(bool enabled) : WebApplicationFactory<Program>
    {
        public RecordingReader Reader { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-key-with-adequate-entropy",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DB_DRIVER"] = "postgres",
                ["AI_IMAGE_DISPATCH_ENABLED"] = enabled ? "true" : "false",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "1"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader>(Reader);
            });
        }
    }

    private sealed class RecordingReader : IQueuedImageGenerationReader
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<QueuedImageGeneration>> ListAsync(int limit, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<QueuedImageGeneration>>([]);
        }
    }
}
