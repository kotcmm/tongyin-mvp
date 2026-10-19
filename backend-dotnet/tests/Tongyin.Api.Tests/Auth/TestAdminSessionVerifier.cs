using Tongyin.Application.Admin;

namespace Tongyin.Api.Tests.Auth;

/// <summary>仅用于既有接口鉴权测试的固定会话验证桩；生产环境不能使用。</summary>
public sealed class TestAdminSessionVerifier(string? validToken) : IAdminSessionVerifier
{
    /// <summary>只接受测试工厂设置的有效 Bearer 凭据，返回管理员身份。</summary>
    public Task<string?> VerifySessionAsync(string? token, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(validToken is not null && token == validToken ? "admin-token" : null);
}
