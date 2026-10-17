using System.Text.Json.Serialization;

namespace Tongyin.Application.Checkout;

/// <summary>订单状态变更日志，兼容旧 Go JSON。</summary>
public sealed class OrderHistorySnapshot
{
    /// <summary>操作发生时间。</summary>
    [JsonPropertyName("at")]
    public string At { get; init; } = "";

    /// <summary>执行者标识。</summary>
    [JsonPropertyName("actor")]
    public string Actor { get; init; } = "";

    /// <summary>操作类型。</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    /// <summary>可选操作说明。</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Note { get; init; }
}
