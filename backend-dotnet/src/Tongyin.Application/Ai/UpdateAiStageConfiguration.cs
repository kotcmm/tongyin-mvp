namespace Tongyin.Application.Ai;

/// <summary>管理员更新单个生成阶段的配置信息；API Key 留空表示保留已有值。</summary>
public sealed class UpdateAiStageConfiguration
{
    /// <summary>固定适配器的 HTTPS 接口。</summary>
    public required string Endpoint { get; init; }
    /// <summary>服务商模型标识。</summary>
    public required string Model { get; init; }
    /// <summary>是否启用下一批生成任务。</summary>
    public required bool Enabled { get; init; }
    /// <summary>新 API Key；留空时不覆盖旧配置。</summary>
    public string? ApiKey { get; init; }
    /// <summary>任务提示词，保存后供新任务使用。</summary>
    public required string PromptTemplate { get; init; }
}
