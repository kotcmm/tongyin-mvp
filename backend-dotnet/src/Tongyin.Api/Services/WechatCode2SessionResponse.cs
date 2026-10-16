using System.Text.Json.Serialization;

namespace Tongyin.Api.Services;

/// <summary>
/// 微信 code2Session 响应，仅映射业务实际使用的错误码和 OpenID，忽略 session_key。
/// </summary>
internal sealed class WechatCode2SessionResponse
{
    /// <summary>供 System.Text.Json 创建对象。</summary>
    public WechatCode2SessionResponse()
    {
    }

    /// <summary>微信错误码；成功时可能不返回该字段。</summary>
    [JsonPropertyName("errcode")]
    public int? ErrorCode { get; init; }

    /// <summary>微信用户 OpenID。</summary>
    [JsonPropertyName("openid")]
    public string? OpenId { get; init; }
}
