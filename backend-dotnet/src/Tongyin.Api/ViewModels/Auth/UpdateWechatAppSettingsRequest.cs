using System.Text.Json.Serialization;

namespace Tongyin.Api.ViewModels.Auth;

/// <summary>后台修改微信小程序 AppID 和可选的新 AppSecret。</summary>
public sealed class UpdateWechatAppSettingsRequest
{
    /// <summary>微信小程序 AppID。</summary>
    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    /// <summary>只写入的新密钥；留空代表保留旧密钥。</summary>
    [JsonPropertyName("app_secret")]
    public string? AppSecret { get; init; }
}
