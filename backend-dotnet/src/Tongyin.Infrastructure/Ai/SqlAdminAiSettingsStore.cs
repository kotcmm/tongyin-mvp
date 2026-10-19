using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>在已有 ty_ai_providers / ty_ai_prompts 业务表中维护图片、模型阶段的默认服务商与提示词。</summary>
public sealed class SqlAdminAiSettingsStore(
    Func<DbConnection> connectionFactory, GoAiKeyDecryptor protector) : IAdminAiSettingsStore
{
    private const string ProvidersSql = "SELECT id AS Id, payload AS Payload FROM ty_ai_providers ORDER BY created_at, id";

    /// <summary>读取默认（即使当前停用）或历史配置，没有密钥内容返回给调用者。</summary>
    public async Task<AiStageConfiguration> GetAsync(string stage, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var providers = await ReadProvidersAsync(db, null, cancellationToken);
        var provider = FindStageProvider(providers, stage);
        var prompt = await ReadPromptAsync(db, null, stage, cancellationToken);
        return Map(stage, provider?.Provider, prompt);
    }

    /// <summary>将服务商和提示词一次事务提交，保留旧密文和不相关阶段配置。</summary>
    public async Task<AiStageConfiguration> SaveAsync(
        string stage, UpdateAiStageConfiguration request, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id=id WHERE id=1", transaction: transaction,
            cancellationToken: cancellationToken));

        var providers = await ReadProvidersAsync(db, transaction, cancellationToken);
        var previous = FindStageProvider(providers, stage);
        var keyCipher = string.IsNullOrWhiteSpace(request.ApiKey)
            ? previous?.Provider.ApiKeyCipher ?? string.Empty
            : protector.Encrypt(request.ApiKey);
        if (request.Enabled && string.IsNullOrWhiteSpace(keyCipher))
        {
            throw new InvalidOperationException("启用前需要先填写 API Key");
        }

        var adapter = stage == "image" ? "seedream" : "hunyuan3d";
        var id = previous?.Id ?? Guid.NewGuid().ToString("N");
        var updated = new AiProviderSettings
        {
            Id = id, Stage = stage, Adapter = adapter,
            Endpoint = request.Endpoint.Trim(), Model = request.Model.Trim(),
            Enabled = request.Enabled, Default = true, ApiKeyCipher = keyCipher
        };
        var payload = JsonSerializer.Serialize(updated);
        if (previous is null)
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_ai_providers(id,owner_id,created_at,payload) VALUES (@Id,@Owner,@Created,@Payload)",
                new { Id = id, Owner = "admin", Created = DateTimeOffset.UtcNow.ToString("O"), Payload = payload },
                transaction: transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_ai_providers SET payload=@Payload WHERE id=@Id",
                new { Id = id, Payload = payload }, transaction: transaction, cancellationToken: cancellationToken));
        }

        // 同一阶段只能有一个默认服务商，其他历史配置不删除。
        foreach (var extra in providers.Where(x => x.Id != id && x.Provider.Stage == stage && x.Provider.Default))
        {
            var old = extra.Provider;
            var disabledDefault = new AiProviderSettings
            {
                Id = old.Id, Stage = old.Stage, Adapter = old.Adapter,
                Endpoint = old.Endpoint, Model = old.Model,
                Enabled = old.Enabled, Default = false, ApiKeyCipher = old.ApiKeyCipher
            };
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_ai_providers SET payload=@Payload WHERE id=@Id",
                new { extra.Id, Payload = JsonSerializer.Serialize(disabledDefault) },
                transaction: transaction, cancellationToken: cancellationToken));
        }

        var previousPrompt = await ReadPromptAsync(db, transaction, stage, cancellationToken);
        var oldPrompt = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_prompts WHERE id=@Stage",
            new { Stage = stage }, transaction: transaction, cancellationToken: cancellationToken));
        var version = oldPrompt is null ? 1 : previousPrompt.Version + 1;
        var promptPayload = JsonSerializer.Serialize(new
        {
            stage, template = request.PromptTemplate.Trim(), version
        });
        if (oldPrompt is null)
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_ai_prompts(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
                new { Id = stage, Owner = "admin", Created = DateTimeOffset.UtcNow.ToString("O"), Payload = promptPayload },
                transaction: transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await db.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_ai_prompts SET payload=@Payload WHERE id=@Id",
                new { Id = stage, Payload = promptPayload }, transaction: transaction,
                cancellationToken: cancellationToken));
        }

        var auditPayload = JsonSerializer.Serialize(new
        {
            actor = "admin", action = "ai.settings.updated", stage,
            enabled = request.Enabled, adapter, provider_id = id, prompt_version = version,
            key_replaced = !string.IsNullOrWhiteSpace(request.ApiKey)
        });
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_audit(id,owner_id,created_at,payload) VALUES(@Id,@Owner,@Created,@Payload)",
            new { Id = Guid.NewGuid().ToString("N"), Owner = "admin",
                Created = DateTimeOffset.UtcNow.ToString("O"), Payload = auditPayload },
            transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Map(stage, updated, (request.PromptTemplate.Trim(), version));
    }

    private static async Task<List<ProviderRow>> ReadProvidersAsync(
        DbConnection db, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<ProviderPayloadRow>(new CommandDefinition(
            ProvidersSql, transaction: transaction, cancellationToken: cancellationToken));
        return rows.Select(row => new ProviderRow(row.Id,
            JsonSerializer.Deserialize<AiProviderSettings>(row.Payload)
            ?? throw new JsonException("AI 服务商 JSON 配置错误"))).ToList();
    }

    private static ProviderRow? FindStageProvider(IEnumerable<ProviderRow> providers, string stage)
        => providers.Where(x => x.Provider.Stage == stage)
            .OrderByDescending(x => x.Provider.Default && x.Provider.Enabled)
            .ThenByDescending(x => x.Provider.Default)
            .FirstOrDefault();

    private static async Task<(string Template, int Version)> ReadPromptAsync(
        DbConnection db, DbTransaction? transaction, string stage, CancellationToken cancellationToken)
    {
        var text = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_prompts WHERE id=@Stage", new { Stage = stage },
            transaction: transaction, cancellationToken: cancellationToken));
        if (text is null)
        {
            return (stage == "image"
                ? SqlAiGenerationSettingsReader.ImageDefaultPrompt
                : SqlAiGenerationSettingsReader.ModelDefaultPrompt, 1);
        }

        using var data = JsonDocument.Parse(text);
        var root = data.RootElement;
        return (root.GetProperty("template").GetString() ?? string.Empty,
            root.GetProperty("version").GetInt32());
    }

    private static AiStageConfiguration Map(string stage, AiProviderSettings? provider,
        (string Template, int Version) prompt) => new()
    {
        Stage = stage,
        Adapter = stage == "image" ? "seedream" : "hunyuan3d",
        Endpoint = provider?.Endpoint ?? (stage == "image"
            ? "https://ark.cn-beijing.volces.com/api/v3/images/generations" : "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit"),
        Model = provider?.Model ?? string.Empty,
        Enabled = provider?.Enabled == true && provider.Default,
        HasApiKey = !string.IsNullOrWhiteSpace(provider?.ApiKeyCipher),
        PromptTemplate = prompt.Template,
        PromptVersion = prompt.Version
    };

    private sealed class ProviderPayloadRow
    {
        public string Id { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    private sealed record ProviderRow(string Id, AiProviderSettings Provider);
}
