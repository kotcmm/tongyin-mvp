using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Hosting;

/// <summary>后台定时回收已删除作品且没有其它业务引用的本地图片/GLB，失败后保留队列供重试。</summary>
public sealed class MediaCleanupPollingService(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaCleanupPollingService> logger) : BackgroundService
{
    /// <summary>每次定时扫描有限数量的持久化删除任务，避免占用业务线程。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // 每轮最多处理 16 个，避免大型媒体目录的批量清理占满 DB 连接。
                    for (var i = 0; i < 16; i++)
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var cleaner = scope.ServiceProvider.GetRequiredService<PendingMediaCleanup>();
                        if (!await cleaner.ProcessNextAsync(stoppingToken))
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    logger.LogWarning("待删除媒体清理失败，已保留任务供重试（{ErrorType}）", error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 应用退出；未处理的任务留在数据库中，重启后继续执行。
        }
    }
}
