using Microsoft.AspNetCore.Authorization;

namespace Tongyin.Api.Auth;

/// <summary>模型服务回调专用授权标记，禁止微信会话或管理员令牌替代 worker 凭据。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AiWebhookAuthorizeAttribute : AuthorizeAttribute
{
    /// <summary>使用独立的 AI 回调授权策略。</summary>
    public AiWebhookAuthorizeAttribute() => Policy = AuthorizationPolicies.AiWebhook;
}
