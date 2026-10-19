namespace Tongyin.Application.Admin;

/// <summary>独立管理账号的持久化凭据；绝不存放明文密码。</summary>
public sealed record AdminCredentials
{
    /// <summary>管理员登录用户名。</summary>
    public required string Username { get; init; }
    /// <summary>每个密码独立生成的随机盐（Base64）。</summary>
    public required string PasswordSalt { get; init; }
    /// <summary>PBKDF2-SHA256 密码摘要（Base64）。</summary>
    public required string PasswordHash { get; init; }
    /// <summary>每次修改凭据或主动退出时轮换，使以前所有会话失效。</summary>
    public required string SessionVersion { get; init; }
}
