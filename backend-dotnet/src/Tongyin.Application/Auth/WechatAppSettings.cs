namespace Tongyin.Application.Auth;

/// <summary>微信小程序服务端应用凭据，只能在可信的后端服务间传递。</summary>
public sealed class WechatAppSettings
{
    /// <summary>微信小程序 AppID。</summary>
    public required string AppId { get; init; }

    /// <summary>已解密的微信小程序 AppSecret，不允许输出到响应或日志。</summary>
    public required string AppSecret { get; init; }
}
