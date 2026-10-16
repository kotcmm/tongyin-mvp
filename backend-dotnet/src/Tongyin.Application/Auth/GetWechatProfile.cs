namespace Tongyin.Application.Auth;

/// <summary>按已认证的微信用户标识查询本人档案及作品订单数量。</summary>
public sealed class GetWechatProfile(IWechatUserRepository userRepository)
{
    /// <summary>读取指定微信身份所拥有的用户档案，身份来源应为服务端认证管线。</summary>
    public Task<WechatUserProfile?> ExecuteAsync(string openId, CancellationToken cancellationToken = default)
        => userRepository.GetProfileAsync(openId, cancellationToken);
}
