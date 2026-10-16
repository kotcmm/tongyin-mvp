namespace Tongyin.Application.Auth;

/// <summary>
/// 微信登录用例：兑换微信身份、首次创建用户、拒绝停用账户、签发服务端会话。
/// </summary>
public sealed class LoginWechat(IWechatCodeExchanger codeExchanger, IWechatUserRepository userRepository, IWechatSessionIssuer sessionIssuer)
{
    /// <summary>使用临时 code 完成登录并签发七天有效的服务端 Token。</summary>
    public async Task<string> ExecuteAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("微信登录凭证不能为空", nameof(code));
        }

        var openId = await codeExchanger.ExchangeAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(openId))
        {
            throw new InvalidOperationException("微信登录未返回用户标识");
        }

        if (!await userRepository.FindOrCreateEnabledAsync(openId, cancellationToken))
        {
            throw new UnauthorizedAccessException("账户已停用");
        }

        return sessionIssuer.Issue(openId, DateTimeOffset.UtcNow, TimeSpan.FromDays(7));
    }
}
