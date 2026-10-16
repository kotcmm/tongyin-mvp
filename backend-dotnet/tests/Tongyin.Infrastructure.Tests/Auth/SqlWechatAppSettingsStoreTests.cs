using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Infrastructure.Auth;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Auth;

/// <summary>微信应用密钥使用独立用途的 AES-GCM 加密，数据库泄露时不应包含 AppSecret 明文。</summary>
public sealed class SqlWechatAppSettingsStoreTests
{
    /// <summary>写入后读回原始凭据，数据库仅存密文。</summary>
    [Fact]
    public async Task SaveAndRead_EncryptsAppSecretAtRest()
    {
        using var db = new Fixture();
        var store = db.Store();
        await store.SaveAsync("wx-id", "app-secret");
        var result = await store.GetAsync();
        Assert.Equal("wx-id", result?.AppId);
        Assert.Equal("app-secret", result?.AppSecret);
        Assert.DoesNotContain("app-secret", db.Payload());
    }

    /// <summary>不传新密钥时保留旧密文，修改 AppID 不会把旧密钥清空。</summary>
    [Fact]
    public async Task SaveWithoutSecret_PreservesOldSecret()
    {
        using var db = new Fixture();
        var store = db.Store();
        await store.SaveAsync("wx-original", "original-secret");
        await store.SaveAsync("wx-updated", null);
        var result = await store.GetAsync();
        Assert.Equal("wx-updated", result?.AppId);
        Assert.Equal("original-secret", result?.AppSecret);
    }

    /// <summary>使用错误的服务器主密钥，无法解密数据库中原有 AppSecret。</summary>
    [Fact]
    public async Task WrongRootSecret_CannotDecrypt()
    {
        using var db = new Fixture();
        await db.Store().SaveAsync("wx-id", "app-secret");
        var wrong = new SqlWechatAppSettingsStore(db.Connect,
            new WechatAppSecretProtector("another-long-test-session-secret"));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => wrong.GetAsync());
    }

    /// <summary>首次读取尚未保存的微信配置应返回空值而非虚假默认凭据。</summary>
    [Fact]
    public async Task MissingSettings_ReturnsNull()
    {
        using var db = new Fixture();
        Assert.Null(await db.Store().GetAsync());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-wxsecret-" + Guid.NewGuid().ToString("N") + ".db");

        public Fixture()
        {
            using var db = Connect();
            db.Open();
            db.Execute("CREATE TABLE ty_system_settings(id TEXT PRIMARY KEY, payload TEXT NOT NULL);" +
                "CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);INSERT INTO ty_store_lock(id) VALUES (1)");
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public SqlWechatAppSettingsStore Store()
            => new(Connect, new WechatAppSecretProtector("test-long-session-secret-12345"));

        public string Payload()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_system_settings WHERE id='wechat_miniapp'");
        }

        public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }
    }
}
