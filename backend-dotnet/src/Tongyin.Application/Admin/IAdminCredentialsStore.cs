namespace Tongyin.Application.Admin;

/// <summary>单管理员账户的数据库持久化边界，不暴露数据库实现给业务用例。</summary>
public interface IAdminCredentialsStore
{
    /// <summary>读取现有管理员账户，尚未初始化时返回 null。</summary>
    Task<AdminCredentials?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>仅在没有账户时完成首次初始化，不能覆盖已建立的管理员账户。</summary>
    Task<bool> CreateIfMissingAsync(AdminCredentials account, CancellationToken cancellationToken = default);

    /// <summary>按会话版本原子更新账户，避免并发修改覆盖较新密码。</summary>
    Task<bool> UpdateIfVersionAsync(AdminCredentials updated, string previousVersion, CancellationToken cancellationToken = default);
}
