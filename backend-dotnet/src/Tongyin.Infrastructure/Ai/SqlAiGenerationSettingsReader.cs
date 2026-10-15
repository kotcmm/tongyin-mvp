using System.Data.Common;
using System.Text.Json;
using Dapper;
using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 通过 Dapper 读取 Go 现有 ty_ai_providers、ty_ai_prompts 表的配置 JSON。
/// </summary>
public sealed class SqlAiGenerationSettingsReader(Func<DbConnection> connectionFactory) : IAiGenerationSettingsReader
{
    private const string ImageDefaultPrompt = "严格参考上传的儿童画，保留原画主体的轮廓、颜色、标志性装饰和稚拙特色，转换成单个全身、正面、立体感清晰的可爱卡通角色；纯净背景，造型简单完整，避免细小悬空零件，适合进一步生成3D打印白模。只输出一张图片，不添加文字、水印或额外角色。";
    private const string ModelDefaultPrompt = "将参考卡通形象转换为单个可3D打印的完整角色模型，保留脸部和服饰的核心辨识特征，结构完整稳定，避免悬浮部件、过薄的四肢和无法打印的细节；输出可预览的 GLB 模型。";

    /// <summary>
    /// 读取指定阶段已启用的默认服务商和提示词，不解密、不输出 API Key。
    /// </summary>
    public async Task<AiGenerationSettings> GetAsync(string stage, CancellationToken cancellationToken = default)
    {
        if (stage is not ("image" or "model"))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), "仅支持 image 或 model 生成阶段");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        var providerPayloads = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_providers ORDER BY created_at, id",
            cancellationToken: cancellationToken));

        AiProviderSettings? provider = null;
        foreach (var payload in providerPayloads)
        {
            var candidate = JsonSerializer.Deserialize<AiProviderSettings>(payload)
                ?? throw new JsonException("AI 服务商配置不是合法的 JSON 对象");
            if (candidate.Stage == stage && candidate.Enabled && candidate.Default)
            {
                provider = candidate;
                break;
            }
        }

        var promptPayload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_ai_prompts WHERE id = @Stage",
            new { Stage = stage },
            cancellationToken: cancellationToken));

        var prompt = stage == "image" ? ImageDefaultPrompt : ModelDefaultPrompt;
        var version = 1;
        if (promptPayload is not null)
        {
            using var document = JsonDocument.Parse(promptPayload);
            prompt = document.RootElement.GetProperty("template").GetString()
                ?? throw new JsonException("AI 提示词不能为空");
            version = document.RootElement.GetProperty("version").GetInt32();
        }

        return new AiGenerationSettings
        {
            Stage = stage,
            Provider = provider,
            PromptTemplate = prompt,
            PromptVersion = version
        };
    }
}
