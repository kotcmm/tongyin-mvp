namespace Tongyin.Application.Admin;

/// <summary>管理员会话验证端口，由具体账号认证用例实施。</summary>
public interface IAdminSessionVerifier
{
    /// <summary>验证已签名会话与数据库版本，返回管理员用户名或 null。</summary>
    Task<string?> VerifySessionAsync(string? token, CancellationToken cancellationToken = default);
}
