namespace Tongyin.Api.ViewModels.Admin;

/// <summary>后台用户名和密码登录请求，不接收或返回任何固定管理员令牌。</summary>
public sealed class AdminLoginRequest
{
    /// <summary>管理员用户名。</summary>
    public string Username { get; set; } = string.Empty;
    /// <summary>管理员密码。</summary>
    public string Password { get; set; } = string.Empty;
}
