using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 通过 Dapper 读取 童印现有 ty_ai_providers、ty_ai_prompts 表的配置 JSON。
/// </summary>
public sealed class SqlAiGenerationSettingsReader(Func<DbConnection> connectionFactory) : IAiGenerationSettingsReader
{
    public const string ImageDefaultPrompt = "严格参考上传的儿童画，保留原画主体的轮廓、颜色、标志性装饰和稚拙特色，转换成单个全身、正面、立体感清晰的可爱卡通角色；纯净背景，造型简单完整，避免细小悬空零件，适合进一步生成3D打印白模。只输出一张图片，不添加文字、水印或额外角色。";
    public const string ModelDefaultPrompt = "将参考卡通形象转换为单个可3D打印的完整角色模型，保留脸部和服饰的核心辨识特征，结构完整稳定，避免悬浮部件、过薄的四肢和无法打印的细节；输出可预览的 GLB 模型。";

    /// <summary>
    /// 读取指定阶段已启用的默认服务商及提示词，保持 运营配置格式不变。
    /// </summary>
    public async Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
    {
        if (stage is not ("image" or "model"))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), "仅支持 image 或 model 生成阶段");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);

        var provider = await GetDefaultProviderAsync(connection, stage, cancellationToken);
        var (template, version) = await GetPromptAsync(connection, stage, cancellationToken);

        return new AiGenerationSettings
        {
            Stage = stage,
            Provider = provider,
            PromptTemplate = template,
            PromptVersion = version
        };
    }

    private static async Task<AiProviderSettings?> GetDefaultProviderAsync(DbConnection connection, string stage, CancellationToken cancellationToken)
    {
        // 配置存储为跨数据库兼容的 TEXT JSON，不能依赖单一数据库的 JSON SQL 方言。
        var payloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_providers ORDER BY created_at, id",
            cancellationToken: cancellationToken));

        foreach (var payload in payloads)
        {
            var provider = JsonSerializer.Deserialize<AiProviderSettings>(payload)
                ?? throw new JsonException("AI 服务商配置不是合法的 JSON 对象");

            if (provider.Stage == stage && provider.Enabled && provider.Default)
            {
                return provider;
            }
        }

        return null;
    }

    private static async Task<(string Template, int Version)> GetPromptAsync(DbConnection connection, string stage, CancellationToken cancellationToken)
    {
        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_prompts WHERE id = @Stage",
            new { Stage = stage },
            cancellationToken: cancellationToken));

        if (payload is null)
        {
            return (stage == "image" ? ImageDefaultPrompt : ModelDefaultPrompt, 1);
        }

        using var document = JsonDocument.Parse(payload);
        var template = document.RootElement.GetProperty("template").GetString()
            ?? throw new JsonException("AI 提示词不能为空");
        var version = document.RootElement.GetProperty("version").GetInt32();
        return (template, version);
    }
}
