using System.Text.Json.Serialization;

namespace Tongyin.Application.Ai;

/// <summary>
/// 兼容 Go 运营后台配置格式的 AI 服务商信息，API Key 始终保留为加密密文。
/// </summary>
public sealed class AiProviderSettings
{
    /// <summary>服务商配置标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>生成能力阶段（image 或 model）。</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    /// <summary>适配器类型，例如 seedream 或 worker。</summary>
    [JsonPropertyName("adapter")]
    public string Adapter { get; init; } = string.Empty;

    /// <summary>服务商调用地址。</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>服务商模型标识。</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>配置是否启用。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>是否为该阶段的默认服务商。</summary>
    [JsonPropertyName("default")]
    public bool Default { get; init; }

    /// <summary>由 Go 运营后台加密的 API Key 密文，不得直接发送到客户端或写入日志。</summary>
    [JsonPropertyName("api_key_cipher")]
    public string ApiKeyCipher { get; init; } = string.Empty;
}
