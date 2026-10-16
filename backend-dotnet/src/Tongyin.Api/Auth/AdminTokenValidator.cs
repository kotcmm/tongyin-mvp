using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Api.Auth;

/// <summary>
/// 验证与 Go 运营后台兼容的 X-Admin-Token；未设置 ADMIN_TOKEN 时始终拒绝访问。
/// </summary>
public sealed class AdminTokenValidator(string? configuredToken)
{
    /// <summary>
    /// 使用固定时间字节比较校验管理员令牌，避免普通微信会话访问跨用户任务信息。
    /// </summary>
    public bool IsValid(string? submittedToken)
    {
        if (string.IsNullOrWhiteSpace(configuredToken) || string.IsNullOrEmpty(submittedToken))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configuredToken),
            Encoding.UTF8.GetBytes(submittedToken));
    }
}
