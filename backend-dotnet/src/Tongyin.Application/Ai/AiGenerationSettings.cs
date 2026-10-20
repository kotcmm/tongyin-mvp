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

    /// <summary>只判断业务阶段与管理员启用状态；协议、地址、凭据由 Adapter 在认领前校验。</summary>
    public bool CanGenerateModel()
        => Stage == "model" && Provider is { Stage: "model", Enabled: true, Default: true };
}
