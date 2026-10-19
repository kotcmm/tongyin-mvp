using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tongyin.Application.Admin;

/// <summary>单管理员用户名密码登录、凭据变更及签名会话管理；不依赖 HTTP 或具体数据库。</summary>
public sealed class AdminAccountService(IAdminCredentialsStore store, string secret, string? bootstrapUsername, string? bootstrapPassword)
{
    private const int Iterations = 210_000;
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(8);
    private readonly byte[] _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes("tongyin-admin-session-v1:" + secret));

    /// <summary>首次在未建立管理员账户时凭服务器配置初始化，后续只验证数据库保存的密码。</summary>
    public async Task<(string Token, string Username)?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (!ValidUsername(username) || string.IsNullOrEmpty(password) || password.Length > 128)
        {
            return null;
        }

        var account = await store.GetAsync(cancellationToken);
        if (account is null)
        {
            if (string.IsNullOrEmpty(bootstrapUsername) || string.IsNullOrEmpty(bootstrapPassword)
                || !ValidUsername(bootstrapUsername) || !ValidPassword(bootstrapPassword)
                || !SecureEquals(username, bootstrapUsername) || !SecureEquals(password, bootstrapPassword))
            {
                return null;
            }

            account = CreateCredentials(bootstrapUsername, bootstrapPassword);
            if (!await store.CreateIfMissingAsync(account, cancellationToken))
            {
                account = await store.GetAsync(cancellationToken);
            }
        }

        if (account is null || !SecureEquals(username, account.Username) || !CheckPassword(password, account))
        {
            return null;
        }

        return (IssueToken(account.SessionVersion), account.Username);
    }

    /// <summary>验证管理员专用 Bearer 会话；必须同时通过签名、时效和当前数据库版本校验。</summary>
    public async Task<string?> VerifySessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
        {
            return null;
        }

        var split = token.Split('.');
        if (split.Length != 2)
        {
            return null;
        }

        byte[] payload;
        byte[] suppliedSignature;
        try
        {
            payload = Convert.FromBase64String(Pad(split[0].Replace('-', '+').Replace('_', '/')));
            suppliedSignature = Convert.FromBase64String(Pad(split[1].Replace('-', '+').Replace('_', '/')));
        }
        catch (FormatException)
        {
            return null;
        }

        var expectedSignature = HMACSHA256.HashData(_signingKey, payload);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
        {
            return null;
        }

        var segments = Encoding.UTF8.GetString(payload).Split('|');
        if (segments.Length != 4 || segments[0] != "1"
            || !long.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        var account = await store.GetAsync(cancellationToken);
        return account is not null && SecureEquals(segments[1], account.SessionVersion) ? account.Username : null;
    }

    /// <summary>检查当前密码后修改用户名或密码；成功时轮换版本并让所有旧会话立即失效。</summary>
    public async Task<string> UpdateAsync(string currentPassword, string? newUsername, string? newPassword, CancellationToken cancellationToken = default)
    {
        var account = await store.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("后台管理员尚未初始化");
        if (string.IsNullOrEmpty(currentPassword) || !CheckPassword(currentPassword, account))
        {
            throw new UnauthorizedAccessException("当前密码不正确");
        }

        if (newUsername is null && newPassword is null)
        {
            throw new ArgumentException("请至少修改用户名或密码");
        }

        var username = newUsername ?? account.Username;
        if (!ValidUsername(username))
        {
            throw new ArgumentException("用户名只能使用 3–40 位英文字母、数字、点、下划线或横线");
        }

        if (newPassword is not null && !ValidPassword(newPassword))
        {
            throw new ArgumentException("新密码长度须为 12–128 位");
        }

        if (username == account.Username && newPassword is null)
        {
            throw new ArgumentException("新用户名不能与当前用户名相同");
        }

        var updated = newPassword is null ? account with { Username = username } : CreateCredentials(username, newPassword);
        updated = updated with { SessionVersion = Guid.NewGuid().ToString("N") };

        if (!await store.UpdateIfVersionAsync(updated, account.SessionVersion, cancellationToken))
        {
            throw new InvalidOperationException("管理员凭据已变化，请重新登录");
        }

        return username;
    }

    /// <summary>退出后废止当前账户全部已签发的会话，包括其他设备中的旧会话。</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var account = await store.GetAsync(cancellationToken);
        if (account is null)
        {
            return;
        }

        await store.UpdateIfVersionAsync(account with { SessionVersion = Guid.NewGuid().ToString("N") },
            account.SessionVersion, cancellationToken);
    }

    private static AdminCredentials CreateCredentials(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hashed = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return new AdminCredentials
        {
            Username = username,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hashed),
            SessionVersion = Guid.NewGuid().ToString("N")
        };
    }

    private static bool CheckPassword(string password, AdminCredentials account)
    {
        var salt = Convert.FromBase64String(account.PasswordSalt);
        var expected = Convert.FromBase64String(account.PasswordHash);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, computed);
    }

    private string IssueToken(string version)
    {
        var expires = DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var payload = Encoding.UTF8.GetBytes("1|" + version + "|" + expires + "|" + Guid.NewGuid().ToString("N"));
        return Encode(payload) + "." + Encode(HMACSHA256.HashData(_signingKey, payload));
    }

    private static bool ValidUsername(string? name)
        => name is { Length: >= 3 and <= 40 }
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pad(string value) => value.PadRight((value.Length + 3) / 4 * 4, '=');

    private static bool SecureEquals(string a, string b)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
