using Tongyin.Application.Works;

namespace Tongyin.Api.Hosting;

/// <summary>轮询模型生成队列，后台提交异步 worker 请求；已认领任务绝不自动重新提交。</summary>
public sealed class ModelGenerationPollingService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ModelGenerationPollingService> logger) : BackgroundService
{
    /// <summary>定期处理一笔模型任务；异常只记录类别，避免将密钥与签名 URL 写入日志。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(configuration.GetValue("AI_MODEL_DISPATCH_POLL_SECONDS", 5), 1, 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    // 必须保证 worker 能从公网回调，避免缺少 PUBLIC_URL 时先认领再挂起。
                    var publicUrl = configuration["PUBLIC_URL"];
                    if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var callbackBase)
                        || callbackBase.Scheme != Uri.UriSchemeHttps
                        || callbackBase.HostNameType != UriHostNameType.Dns
                        || callbackBase.IsLoopback || callbackBase.Port != 443
                        || callbackBase.UserInfo.Length != 0 || callbackBase.Query.Length != 0
                        || callbackBase.Fragment.Length != 0)
                    {
                        continue;
                    }

                    var dispatcher = scope.ServiceProvider.GetRequiredService<DispatchQueuedModelGeneration>();
                    await dispatcher.DispatchNextAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError("3D 模型任务派发异常，类别：{Type}；已认领任务不会自动重试", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 后台宿主关闭。
        }
    }
}
