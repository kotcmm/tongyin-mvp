using Microsoft.Extensions.DependencyInjection;
using Tongyin.Application.Works;
using Tongyin.Application.Ai;

namespace Tongyin.Api.Services;

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
                        continue;
                    }

                    var dispatcher = scope.ServiceProvider.GetRequiredService<DispatchQueuedImageGeneration>();
                    await dispatcher.DispatchNextAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // 上游异常可能含鉴权与签名 URL；不能将它的消息或堆栈原样写入后台日志。
                    logger.LogError("图片生成派发失败，异常类别：{ErrorType}；认领任务不会自动重试", error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 主机关闭时正常退出，不把取消误判为可重新执行的失败。
        }
    }
}
