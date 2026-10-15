using System.Globalization;
using System.Text.Json.Serialization;
using Tongyin.Domain.Works;

namespace Tongyin.Api.Works;

/// <summary>
/// 向微信小程序返回的作品创建响应，保留原 Go 接口字段名称。
/// </summary>
public sealed class WorkResponse
{
    /// <summary>
    /// 作品唯一标识。
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// 创建时间的 RFC3339 文本。
    /// </summary>
    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    /// <summary>
    /// 有效期内可访问的签名原画地址。
    /// </summary>
    [JsonPropertyName("original")]
    public required string Original { get; init; }

    /// <summary>
    /// 当前候选图片地址，创建时为空。
    /// </summary>
    [JsonPropertyName("candidates")]
    public required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>
    /// 选中的候选图下标。
    /// </summary>
    [JsonPropertyName("selected")]
    public required int Selected { get; init; }

    /// <summary>
    /// 已生成的模型预览图地址。
    /// </summary>
    [JsonPropertyName("preview")]
    public required IReadOnlyList<string> Preview { get; init; }

    /// <summary>
    /// 已生成模型的媒体地址。
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// 作品创建后的业务状态。
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// 图片生成的已消耗次数。
    /// </summary>
    [JsonPropertyName("image_attempts")]
    public required int ImageAttempts { get; init; }

    /// <summary>
    /// 当前生成任务编号。
    /// </summary>
    [JsonPropertyName("generation_id")]
    public required string GenerationId { get; init; }

    /// <summary>
    /// 作品备注。
    /// </summary>
    [JsonPropertyName("note")]
    public required string Note { get; init; }

    /// <summary>旧版用户内部标识（存在时才输出）。</summary>
    [JsonPropertyName("owner_user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerUserId { get; init; }

    /// <summary>AI 服务商标识（存在时才输出）。</summary>
    [JsonPropertyName("ai_provider_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiProviderId { get; init; }

    /// <summary>提示词版本（非零时才输出）。</summary>
    [JsonPropertyName("ai_prompt_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AiPromptVersion { get; init; }

    /// <summary>当前 AI 任务阶段（存在时才输出）。</summary>
    [JsonPropertyName("ai_task_stage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiTaskStage { get; init; }

    /// <summary>AI 任务开始时间（存在时才输出）。</summary>
    [JsonPropertyName("ai_started_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiStartedAt { get; init; }

    /// <summary>
    /// 将新创建的作品转换为小程序需要的旧版 JSON 结构，不暴露用户 OpenID。
    /// </summary>
    internal static WorkResponse FromCreated(Work work, string signedOriginal)
    {
        return new WorkResponse
        {
            Id = work.Id,
            CreatedAt = work.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            Original = signedOriginal,
            Candidates = work.Candidates,
            Selected = work.Selected,
            Preview = work.Preview,
            Model = work.Model,
            Status = "image_pending",
            ImageAttempts = work.ImageAttempts,
            GenerationId = work.GenerationId,
            Note = work.Note
        };
    }
    /// <summary>
    /// 查询时重签每个媒体文件，保留作品当前状态与已有 AI 任务信息。
    /// </summary>
    internal static WorkResponse FromSnapshot(WorkSnapshot work, Func<string, string> signUrl)
    {
        static string Signed(string? fileName, Func<string, string> signer)
            => string.IsNullOrEmpty(fileName) ? string.Empty : signer(fileName);

        return new WorkResponse
        {
            Id = work.Id,
            CreatedAt = work.CreatedAt,
            Original = Signed(work.Original, signUrl),
            Candidates = (work.Candidates ?? Array.Empty<string>()).Select(name => Signed(name, signUrl)).ToArray(),
            Selected = work.Selected,
            Preview = (work.Preview ?? Array.Empty<string>()).Select(name => Signed(name, signUrl)).ToArray(),
            Model = Signed(work.Model, signUrl),
            Status = work.Status,
            ImageAttempts = work.ImageAttempts,
            GenerationId = work.GenerationId ?? string.Empty,
            Note = work.Note ?? string.Empty,
            OwnerUserId = string.IsNullOrEmpty(work.OwnerUserId) ? null : work.OwnerUserId,
            AiProviderId = string.IsNullOrEmpty(work.AiProviderId) ? null : work.AiProviderId,
            AiPromptVersion = work.AiPromptVersion == 0 ? null : work.AiPromptVersion,
            AiTaskStage = string.IsNullOrEmpty(work.AiTaskStage) ? null : work.AiTaskStage,
            AiStartedAt = string.IsNullOrEmpty(work.AiStartedAt) ? null : work.AiStartedAt
        };
    }

}
