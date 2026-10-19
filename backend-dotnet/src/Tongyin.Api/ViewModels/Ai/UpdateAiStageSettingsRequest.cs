using System.Text.Json.Serialization;
using Tongyin.Application.Ai;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>管理员保存服务商配置的类型化请求，不能接受任意 JSON 配置对象。</summary>
public sealed class UpdateAiStageSettingsRequest
{
    /// <summary>服务商 HTTPS URL。</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;
    /// <summary>模型标识。</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;
    /// <summary>是否允许下一个任务自动认领。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    /// <summary>空字符串/空字段不修改已有密钥。</summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }
    /// <summary>图片或 3D 模型提示词。</summary>
    [JsonPropertyName("prompt_template")]
    public string PromptTemplate { get; init; } = string.Empty;

    /// <summary>转换为 Application 用例请求对象。</summary>
    public UpdateAiStageConfiguration ToCommand() => new()
    {
        Endpoint = Endpoint, Model = Model, Enabled = Enabled, ApiKey = ApiKey,
        PromptTemplate = PromptTemplate
    };
}
