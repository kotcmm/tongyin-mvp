namespace Tongyin.Application.Ai;

/// <summary>管理员可以读取的生成阶段配置；永远不包含密文或明文 API Key。</summary>
public sealed class AiStageConfiguration
{
    /// <summary>image 或 model。</summary>
    public required string Stage { get; init; }
    /// <summary>实际适配器：Seedream 或符合童印 Worker 协议的模型服务。</summary>
    public required string Adapter { get; init; }
    /// <summary>服务商调用地址。</summary>
    public required string Endpoint { get; init; }
    /// <summary>模型标识。</summary>
    public required string Model { get; init; }
    /// <summary>是否启用自动派发。</summary>
    public required bool Enabled { get; init; }
    /// <summary>已存在加密的 API Key。</summary>
    public required bool HasApiKey { get; init; }
    /// <summary>当前生成提示词。</summary>
    public required string PromptTemplate { get; init; }
    /// <summary>提示词版本。</summary>
    public required int PromptVersion { get; init; }
}
