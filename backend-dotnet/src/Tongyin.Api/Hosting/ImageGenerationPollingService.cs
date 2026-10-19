using Microsoft.Extensions.DependencyInjection;
using Tongyin.Application.Works;
using Tongyin.Application.Ai;

namespace Tongyin.Api.Hosting;

/// <summary>
/// 图片生成后台轮询宿主；按数据库服务商启用状态动态控制派发，不自动解除认领或重试。
/// </summary>
public sealed class ImageGenerationPollingService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ImageGenerationPollingService> logger) : BackgroundService
{
    /// <summary>
    /// 定期调用单次派发用例。错误只记录异常类别，不写含敏感 URL 或 API Key 的异常详情。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var diagnostics = new PollingFailureDiagnostics();
        var pollSeconds = Math.Clamp(configuration.GetValue("AI_IMAGE_DISPATCH_POLL_SECONDS", 5), 1, 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var aiSettingsReader = scope.ServiceProvider.GetRequiredService<IAiGenerationSettingsReader>();
                    var settings = await aiSettingsReader.GetAsync("image", stoppingToken);
                    if (!settings.CanGenerateImage())
                    {
                        diagnostics.Reset();
                        continue;
                    }

                    var dispatcher = scope.ServiceProvider.GetRequiredService<DispatchQueuedImageGeneration>();
                    await dispatcher.DispatchNextAsync(stoppingToken);
                    diagnostics.Reset();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // 仅使用受控错误类别，不输出来自数据库或第三方服务商的原始消息与堆栈。
                    var reason = PollingFailureDiagnostics.Describe(error);
                    if (diagnostics.ShouldLog(reason, DateTimeOffset.UtcNow))
                    {
                        logger.LogError("图片生成派发失败：{Reason}（{ErrorType}）；后续相同错误每分钟最多提示一次，已认领任务不会自动重试", reason, error.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 主机关闭时正常退出，不把取消误判为可重新执行的失败。
        }
    }
}
