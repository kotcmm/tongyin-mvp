using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Identity;

namespace Tongyin.Infrastructure.Auth;

/// <summary>
/// Identity 标准 IUserStore 适配童印现有 Dapper/PostgreSQL/MySQL 数据访问。
/// 密码的生成、哈希验证、锁定及认证全由 ASP.NET Core Identity 负责。
/// </summary>
public sealed class SqlIdentityUserStore(Func<DbConnection> connectionFactory)
    : IUserPasswordStore<IdentityUser>, IUserSecurityStampStore<IdentityUser>, IUserLockoutStore<IdentityUser>
{
    private bool _disposed;

    /// <summary>用户主键沿用 IdentityUser 的字符串 ID。</summary>
    public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id);

    /// <summary>读取用户登录名。</summary>
    public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    /// <summary>Identity 用户管理器更新登录名。</summary>
    public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    /// <summary>读取规范化后的登录名。</summary>
    public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    /// <summary>写入规范化后的登录名。</summary>
    public Task SetNormalizedUserNameAsync(IdentityUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    /// <summary>只允许首次插入固定 ID 的管理员账户。</summary>
    public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (user.Id != "admin") return IdentityResult.Failed(new IdentityError { Description = "只允许初始化固定管理员账户" });
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        try
        {
            var rows = await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ty_admin_identity
                  (id,user_name,normalized_user_name,password_hash,security_stamp,concurrency_stamp,
                   access_failed_count,lockout_end,lockout_enabled)
                VALUES (@Id,@UserName,@NormalizedUserName,@PasswordHash,@SecurityStamp,@ConcurrencyStamp,
                        @AccessFailedCount,@LockoutEnd,@LockoutEnabled)
                """, ToRow(user), cancellationToken: cancellationToken));
            return rows == 1 ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Description = "初始化管理员账户失败" });
        }
        catch (DbException)
        {
            return IdentityResult.Failed(new IdentityError { Description = "管理员账户已存在或数据库暂不可用" });
        }
    }

    /// <summary>并发戳校验保证 Identity 用户修改不会覆盖另一请求的更新。</summary>
    public async Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var previousStamp = user.ConcurrencyStamp;
        var nextStamp = Guid.NewGuid().ToString("N");
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var rows = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE ty_admin_identity SET
              user_name=@UserName, normalized_user_name=@NormalizedUserName,
              password_hash=@PasswordHash, security_stamp=@SecurityStamp,
              concurrency_stamp=@NextStamp, access_failed_count=@AccessFailedCount,
              lockout_end=@LockoutEnd, lockout_enabled=@LockoutEnabled
            WHERE id=@Id AND concurrency_stamp=@PreviousStamp
            """, new
        {
            user.Id, user.UserName, user.NormalizedUserName, user.PasswordHash, user.SecurityStamp,
            NextStamp = nextStamp, PreviousStamp = previousStamp,
            user.AccessFailedCount, LockoutEnd = DateValue(user.LockoutEnd),
            LockoutEnabled = user.LockoutEnabled ? 1 : 0
        }, cancellationToken: cancellationToken));
        if (rows != 1)
        {
            return IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "账号已更新，请重试" });
        }
        user.ConcurrencyStamp = nextStamp;
        return IdentityResult.Success;
    }

    /// <summary>不允许后台删除唯一管理员，以免失去管理权限。</summary>
    public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "不能删除唯一管理员" }));

    /// <summary>按主键读取管理员及 Identity 密码、锁定、安全戳信息。</summary>
    public async Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<IdentityRow>(new CommandDefinition(
            Select + " WHERE id=@Id", new { Id = userId }, cancellationToken: cancellationToken));
        return row is null ? null : FromRow(row);
    }

    /// <summary>按规范化用户名查询；不绕过 Identity 的用户名校验。</summary>
    public async Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<IdentityRow>(new CommandDefinition(
            Select + " WHERE normalized_user_name=@Name", new { Name = normalizedUserName }, cancellationToken: cancellationToken));
        return row is null ? null : FromRow(row);
    }

    /// <summary>保留 Identity 生成的单字段密码哈希，内部包含随机盐与迭代参数。</summary>
    public Task SetPasswordHashAsync(IdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    /// <summary>Identity 验证密码时读取其专用密码哈希格式。</summary>
    public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PasswordHash);

    /// <summary>判断是否已设置有效密码。</summary>
    public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    /// <summary>安全戳由 Identity 更新，用于使旧 Cookie 会话失效。</summary>
    public Task SetSecurityStampAsync(IdentityUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    /// <summary>返回数据库中当前安全戳。</summary>
    public Task<string?> GetSecurityStampAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.SecurityStamp);

    /// <summary>Identity 锁定账户使用 ISO 8601 标准时间。</summary>
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnd);

    /// <summary>设置账户锁定的截至时间。</summary>
    public Task SetLockoutEndDateAsync(IdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    /// <summary>增加 Identity 管理的失败登录计数。</summary>
    public Task<int> IncrementAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(++user.AccessFailedCount);

    /// <summary>成功登录后清除失败次数。</summary>
    public Task ResetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    /// <summary>读取登录失败计数。</summary>
    public Task<int> GetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.AccessFailedCount);

    /// <summary>读取锁定开关。</summary>
    public Task<bool> GetLockoutEnabledAsync(IdentityUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnabled);

    /// <summary>由 Identity 开启或关闭账户锁定。</summary>
    public Task SetLockoutEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    /// <summary>Store 生命周期由 ASP.NET Core DI 统一管理。</summary>
    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private const string Select = """
        SELECT id AS Id,user_name AS UserName,normalized_user_name AS NormalizedUserName,
          password_hash AS PasswordHash,security_stamp AS SecurityStamp,
          concurrency_stamp AS ConcurrencyStamp,access_failed_count AS AccessFailedCount,
          lockout_end AS LockoutEnd,lockout_enabled AS LockoutEnabled
        FROM ty_admin_identity
        """;

    private static string? DateValue(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static object ToRow(IdentityUser user) => new
    {
        user.Id, user.UserName, user.NormalizedUserName, user.PasswordHash, user.SecurityStamp,
        user.ConcurrencyStamp, user.AccessFailedCount,
        LockoutEnd = DateValue(user.LockoutEnd),
        LockoutEnabled = user.LockoutEnabled ? 1 : 0
    };

    private static IdentityUser FromRow(IdentityRow row) => new()
    {
        Id = row.Id,
        UserName = row.UserName,
        NormalizedUserName = row.NormalizedUserName,
        PasswordHash = row.PasswordHash,
        SecurityStamp = row.SecurityStamp,
        ConcurrencyStamp = row.ConcurrencyStamp,
        AccessFailedCount = row.AccessFailedCount,
        LockoutEnabled = row.LockoutEnabled == 1,
        LockoutEnd = DateTimeOffset.TryParse(row.LockoutEnd, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var locked) ? locked : null
    };

    private sealed class IdentityRow
    {
        public string Id { get; set; } = "";
        public string? UserName { get; set; }
        public string? NormalizedUserName { get; set; }
        public string? PasswordHash { get; set; }
        public string? SecurityStamp { get; set; }
        public string? ConcurrencyStamp { get; set; }
        public int AccessFailedCount { get; set; }
        public string? LockoutEnd { get; set; }
        public int LockoutEnabled { get; set; }
    }
}
