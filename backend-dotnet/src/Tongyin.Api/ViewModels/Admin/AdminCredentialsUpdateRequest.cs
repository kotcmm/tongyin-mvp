namespace Tongyin.Api.ViewModels.Admin;

/// <summary>管理员自行维护账号信息时必须携带当前密码。</summary>
public sealed class AdminCredentialsUpdateRequest
{
    /// <summary>确认当前账户所有权的现有密码。</summary>
    public string CurrentPassword { get; set; } = string.Empty;
    /// <summary>希望改成的新用户名；不修改则省略。</summary>
    public string? NewUsername { get; set; }
    /// <summary>希望改成的新密码；不修改则省略。</summary>
    public string? NewPassword { get; set; }
}
