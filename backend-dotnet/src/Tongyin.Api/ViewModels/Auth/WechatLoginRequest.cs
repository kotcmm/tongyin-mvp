using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Auth;

/// <summary>
/// 微信小程序提交的一次性登录凭证。
/// </summary>
public sealed class WechatLoginRequest
{
    /// <summary>wx.login 返回的一次性 code。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
