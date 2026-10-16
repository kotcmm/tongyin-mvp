namespace Tongyin.Application.Auth;

/// <summary>
/// 微信用户档案的持久化端口。
/// </summary>
public interface IWechatUserRepository
{
    /// <summary>按 OpenID 查找或首次创建用户；已停用的用户返回 false。</summary>
    Task<bool> FindOrCreateEnabledAsync(string openId, CancellationToken cancellationToken = default);

    /// <summary>按微信 OpenID 读取用户公开档案及其作品、订单数量。</summary>
    Task<WechatUserProfile?> GetProfileAsync(string openId, CancellationToken cancellationToken = default);
}
