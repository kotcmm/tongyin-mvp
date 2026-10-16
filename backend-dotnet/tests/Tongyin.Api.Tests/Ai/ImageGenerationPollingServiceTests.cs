using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tongyin.Application.Works;
using Tongyin.Application.Ai;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>
/// 后台轮询根据数据库中的图片服务商状态动态派发，服务商停用时不得扫描任务。
/// </summary>
public sealed class ImageGenerationPollingServiceTests
{
    /// <summary>
    /// 数据库中没有启用的默认图片服务商时，轮询宿主不得扫描待执行任务。
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
    /// 数据库中启用了默认图片服务商后，轮询应按时间间隔扫描待生成任务。
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

    /// <summary>
    /// 更新数据库配置的效果应在下一轮轮询生效，不需要重启服务。
    /// </summary>
    [Fact]
    public async Task HostedService_ProviderSwitch_ChangesPollingWithoutRestart()
    {
        using var app = new PollingFactory(enabled: false);
        using var client = app.CreateClient();
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.Equal(0, app.Reader.Calls);

        app.Settings.Enabled = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (app.Reader.Calls == 0 && !timeout.IsCancellationRequested)
        {
            await Task.Delay(50);
        }

        Assert.True(app.Reader.Calls > 0);
        app.Settings.Enabled = false;
        var count = app.Reader.Calls;
        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        Assert.Equal(count, app.Reader.Calls);
    }

    private sealed class PollingFactory(bool enabled) : WebApplicationFactory<Program>
    {
        public RecordingReader Reader { get; } = new();
        public RecordingSettingsReader Settings { get; } = new(enabled);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-key-with-adequate-entropy",
                ["DB_CONNECTION_STRING"] = "unused-in-test",
                ["DB_DRIVER"] = "postgres",
                ["AI_IMAGE_DISPATCH_POLL_SECONDS"] = "1"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueuedImageGenerationReader>();
                services.AddSingleton<IQueuedImageGenerationReader>(Reader);
                services.RemoveAll<IAiGenerationSettingsReader>();
                services.AddSingleton<IAiGenerationSettingsReader>(Settings);
            });
        }
    }

    private sealed class RecordingSettingsReader(bool enabled) : IAiGenerationSettingsReader
    {
        private int _enabled = enabled ? 1 : 0;

        public bool Enabled
        {
            get => Volatile.Read(ref _enabled) == 1;
            set => Interlocked.Exchange(ref _enabled, value ? 1 : 0);
        }

        public Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiGenerationSettings
            {
                Stage = "image",
                PromptTemplate = "保留儿童画主体",
                PromptVersion = 1,
                Provider = Enabled ? new AiProviderSettings
                {
                    Stage = "image",
                    Adapter = "seedream",
                    Endpoint = "https://ark.cn-beijing.volces.com/api/v3/images/generations",
                    Model = "test-model",
                    ApiKeyCipher = "test-cipher",
                    Enabled = true,
                    Default = true
                } : null
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
