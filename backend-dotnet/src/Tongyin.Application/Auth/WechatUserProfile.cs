namespace Tongyin.Application.Auth;

/// <summary>微信用户可公开给本人查看的档案，不包含 OpenID 或微信密钥。</summary>
public sealed class WechatUserProfile
{
    /// <summary>数据库用户资料中的稳定内部 ID。</summary>
    public required string Id { get; init; }

    /// <summary>用户创建时间，沿用持久化的原始时间格式。</summary>
    public required string CreatedAt { get; init; }

    /// <summary>当前微信用户的作品数量。</summary>
    public required int Works { get; init; }

    /// <summary>当前微信用户的订单数量。</summary>
    public required int Orders { get; init; }
}
