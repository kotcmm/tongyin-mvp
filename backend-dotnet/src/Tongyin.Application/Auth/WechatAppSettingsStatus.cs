namespace Tongyin.Application.Auth;

/// <summary>面向管理后台的非敏感微信配置状态。</summary>
public sealed class WechatAppSettingsStatus
{
    /// <summary>已配置的 AppID。</summary>
    public required string AppId { get; init; }

    /// <summary>是否已保存 AppSecret；不会向客户端提供真实密钥。</summary>
    public required bool HasAppSecret { get; init; }
}
