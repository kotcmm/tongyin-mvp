namespace Tongyin.Application.Ai;

/// <summary>
/// 从已有 Go 配置存储读取 AI 服务商及提示词，不依赖具体数据库驱动。
/// </summary>
public interface IAiGenerationSettingsReader
{
    /// <summary>
    /// 按 image 或 model 阶段读取配置；未配置默认服务商时返回空 Provider。
    /// </summary>
    Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default);
}
