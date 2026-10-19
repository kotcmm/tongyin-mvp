using Tongyin.Application.Works;

namespace Tongyin.Api.Hosting;

/// <summary>轮询模型生成队列，后台提交异步 混元 API 请求；已认领任务绝不自动重新提交。</summary>
public sealed class ModelGenerationPollingService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ModelGenerationPollingService> logger) : BackgroundService
{
    /// <summary>定期处理一笔模型任务；异常只记录类别，避免将密钥与签名 URL 写入日志。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var diagnostics = new PollingFailureDiagnostics();
        var pollSeconds = Math.Clamp(configuration.GetValue("AI_MODEL_DISPATCH_POLL_SECONDS", 5), 1, 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<ModelGenerationService>();
                    await dispatcher.ProcessNextAsync(stoppingToken);
                    diagnostics.Reset();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // 模型服务商的异常正文可能含密钥或带签名的 URL，仅输出固定分类。
                    var reason = PollingFailureDiagnostics.Describe(exception);
                    if (diagnostics.ShouldLog(reason, DateTimeOffset.UtcNow))
                    {
                        logger.LogError("3D 模型任务处理失败：{Reason}（{ErrorType}）；后续相同错误每分钟最多提示一次，已认领任务不会自动重试", reason, exception.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 后台宿主关闭。
        }
    }
}
