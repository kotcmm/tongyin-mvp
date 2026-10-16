namespace Tongyin.Application.Auth;

/// <summary>
/// 服务端会话令牌签发端口，避免应用用例依赖加密实现。
/// </summary>
public interface IWechatSessionIssuer
{
    /// <summary>签发指定用户有效期内的带签名会话令牌。</summary>
    string Issue(string openId, DateTimeOffset now, TimeSpan lifetime);
}
