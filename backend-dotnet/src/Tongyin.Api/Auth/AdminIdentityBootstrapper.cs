using Microsoft.AspNetCore.Identity;

namespace Tongyin.Api.Auth;

/// <summary>只在未建立管理员账户时允许通过私有配置首次初始化，之后全部使用 Identity 数据库账户。</summary>
public sealed class AdminIdentityBootstrapper(UserManager<IdentityUser> users, IConfiguration config)
{
    /// <summary>尝试一次性管理员初始化；配置永远不能重置已有管理员密码。</summary>
    public async Task TryCreateAsync(string username, string password)
    {
        var configuredUser = config["ADMIN_BOOTSTRAP_USER"];
        var configuredPassword = config["ADMIN_BOOTSTRAP_PASSWORD"];

        if (string.IsNullOrWhiteSpace(configuredUser) || string.IsNullOrEmpty(configuredPassword)
            || username != configuredUser || password != configuredPassword
            || configuredPassword.Length < 12
            || await users.FindByIdAsync("admin") is not null)
        {
            return;
        }

        var user = new IdentityUser { Id = "admin", UserName = configuredUser, LockoutEnabled = true };
        await users.CreateAsync(user, configuredPassword);
    }
}
