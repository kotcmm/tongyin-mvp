using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Auth;

namespace Tongyin.Infrastructure.Auth;

/// <summary>在独立的 ty_system_settings 表加密存储微信小程序应用凭据。</summary>
public sealed class SqlWechatAppSettingsStore(Func<DbConnection> connectionFactory, WechatAppSecretProtector protector) : IWechatAppSettingsStore
{
    private const string SettingsId = "wechat_miniapp";

    /// <summary>实时从数据库读取 AppID 并解密 AppSecret；不做进程缓存。</summary>
    public async Task<WechatAppSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_system_settings WHERE id = @Id",
            new { Id = SettingsId }, cancellationToken: cancellationToken));
        return payload is null ? null : Deserialize(payload);
    }

    /// <summary>在数据库事务中保存配置；AppSecret 留空时原密文原样保留。</summary>
    public async Task SaveAsync(string appId, string? appSecret, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));

        var previousPayload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_system_settings WHERE id = @Id",
            new { Id = SettingsId }, transaction: transaction, cancellationToken: cancellationToken));
        var previousCipher = previousPayload is null ? string.Empty : ReadPayload(previousPayload).Cipher;
        var cipher = string.IsNullOrEmpty(appSecret) ? previousCipher : protector.Encrypt(appSecret);
        var payload = JsonSerializer.Serialize(new { app_id = appId, app_secret_cipher = cipher });
        if (previousPayload is null)
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_system_settings (id, payload) VALUES (@Id, @Payload)",
                new { Id = SettingsId, Payload = payload }, transaction: transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_system_settings SET payload = @Payload WHERE id = @Id",
                new { Id = SettingsId, Payload = payload }, transaction: transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private WechatAppSettings Deserialize(string payload)
    {
        var (appId, cipher) = ReadPayload(payload);
        return new WechatAppSettings
        {
            AppId = appId,
            AppSecret = string.IsNullOrEmpty(cipher) ? string.Empty : protector.Decrypt(cipher)
        };
    }

    private static (string AppId, string Cipher) ReadPayload(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("系统微信配置格式无效");
        }

        var appId = root.TryGetProperty("app_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty : string.Empty;
        var cipher = root.TryGetProperty("app_secret_cipher", out var secret) && secret.ValueKind == JsonValueKind.String
            ? secret.GetString() ?? string.Empty : string.Empty;
        return (appId, cipher);
    }
}
