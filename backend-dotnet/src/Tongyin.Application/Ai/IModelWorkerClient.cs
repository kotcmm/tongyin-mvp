using Tongyin.Application.Works;

namespace Tongyin.Application.Ai;

/// <summary>提交异步模型生成任务到已配置的外部 worker，不负责数据库持久化。</summary>
public interface IModelWorkerClient
{
    /// <summary>提交当前已认领作品及其确认图片的签名 URL，等待异步 GLB 回调。</summary>
    Task SubmitAsync(AiGenerationSettings settings, WorkSnapshot work, CancellationToken cancellationToken = default);
}
