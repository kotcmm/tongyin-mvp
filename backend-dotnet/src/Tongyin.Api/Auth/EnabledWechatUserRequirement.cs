using Microsoft.AspNetCore.Authorization;

namespace Tongyin.Api.Auth;

/// <summary>
/// 微信用户必须在现有数据库中登记且账号未停用的授权要求。
/// </summary>
public sealed class EnabledWechatUserRequirement : IAuthorizationRequirement
{
}
