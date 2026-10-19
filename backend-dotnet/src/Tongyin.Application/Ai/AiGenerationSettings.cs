namespace Tongyin.Application.Ai;

/// <summary>
/// 一次生成任务所需的服务商选择和提示词快照，尚不包含实际 HTTP 派发。
/// </summary>
public sealed class AiGenerationSettings
{
    /// <summary>生成能力阶段。</summary>
    public required string Stage { get; init; }

    /// <summary>当前阶段已启用的默认服务商；没有配置则为空。</summary>
    public AiProviderSettings? Provider { get; init; }

    /// <summary>运营后台配置或 Go 内置的提示词正文。</summary>
    public required string PromptTemplate { get; init; }

    /// <summary>当前提示词版本。</summary>
    public required int PromptVersion { get; init; }

    /// <summary>
    /// 判断数据库中是否有可用的默认图片生成服务商；后台停用后即禁止新任务与轮询派发。
    /// </summary>
    public bool CanGenerateImage()
    {
        return Stage == "image"
            && Provider is { Stage: "image", Adapter: "seedream", Enabled: true, Default: true }
            && !string.IsNullOrWhiteSpace(Provider.Endpoint)
            && !string.IsNullOrWhiteSpace(Provider.Model)
            && !string.IsNullOrWhiteSpace(Provider.ApiKeyCipher)
            && !string.IsNullOrWhiteSpace(PromptTemplate);
    }

    /// <summary>只有已启用默认模型 worker 并设置安全密钥与提示词时，才能认领新的模型任务。</summary>
    public bool CanGenerateModel()
    {
        return Stage == "model"
            && Provider is { Stage: "model", Adapter: "hunyuan3d", Enabled: true, Default: true }
            && Provider.Endpoint == "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit"
            && Provider.Model is ("3.0" or "3.1")
            && !string.IsNullOrWhiteSpace(Provider.ApiKeyCipher);
    }
}
