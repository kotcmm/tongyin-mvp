namespace Tongyin.Application.Ai;

/// <summary>管理员配置的持久化端口，基础设施负责加密、事务和向下兼容。</summary>
public interface IAdminAiSettingsStore
{
    /// <summary>返回安全的只读阶段配置。</summary>
    Task<AiStageConfiguration> GetAsync(string stage, CancellationToken cancellationToken = default);

    /// <summary>原子保存默认服务商与提示词，不泄露 API Key。</summary>
    Task<AiStageConfiguration> SaveAsync(string stage, UpdateAiStageConfiguration settings,
        CancellationToken cancellationToken = default);
}
