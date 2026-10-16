namespace Tongyin.Application.Auth;

/// <summary>
/// 把微信小程序的一次性登录 code 兑换为服务端可信的 OpenID。
/// </summary>
public interface IWechatCodeExchanger
{
    /// <summary>调用微信认证服务获取用户标识，不向客户端暴露 session_key。</summary>
    Task<string> ExchangeAsync(string code, CancellationToken cancellationToken = default);
}
