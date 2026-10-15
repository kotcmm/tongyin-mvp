namespace Tongyin.Domain.Works;

/// <summary>
/// 儿童画作品。上传原画后，作品进入等待生成形象图片的初始阶段。
/// </summary>
public sealed class Work
{
    private Work(string id, string owner, DateTimeOffset createdAt, string original)
    {
        Id = id;
        Owner = owner;
        CreatedAt = createdAt;
        Original = original;
    }

    /// <summary>
    /// 作品标识。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 作品所属的微信用户标识。
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 已上传原画的存储路径。
    /// </summary>
    public string Original { get; }

    /// <summary>
    /// 当前状态，初始为等待生成形象图片。
    /// </summary>
    public string Status { get; private set; } = "image_pending";

    /// <summary>
    /// 已选择的候选图索引，负一表示尚未选择。
    /// </summary>
    public int Selected { get; } = -1;

    /// <summary>
    /// 已消耗的图片生成次数。
    /// </summary>
    public int ImageAttempts { get; private set; }

    /// <summary>
    /// 已生成的候选图路径。
    /// </summary>
    public IReadOnlyList<string> Candidates { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 模型预览图路径。
    /// </summary>
    public IReadOnlyList<string> Preview { get; } = Array.Empty<string>();

    /// <summary>
    /// 最近一次形象图片生成失败的原因，初始为空。
    /// </summary>
    public string Note { get; private set; } = string.Empty;

    /// <summary>
    /// 当前形象图片生成任务标识，初始为空。
    /// </summary>
    public string GenerationId { get; private set; } = string.Empty;

    /// <summary>
    /// 请求生成形象图片，失败后可在剩余额度内再次提交。
    /// </summary>
    public void RequestImageGeneration(string generationId, int freeImageAttempts = 2)
    {
        if (Status == "image_pending" && ImageAttempts > 0)
        {
            throw new InvalidOperationException("形象正在生成，请勿重复提交");
        }

        if (ImageAttempts >= freeImageAttempts)
        {
            throw new InvalidOperationException("免费生成次数已用完");
        }

        ImageAttempts++;
        GenerationId = generationId;
        Status = "image_pending";
        Candidates = Array.Empty<string>();
        Note = string.Empty;
    }

    /// <summary>
    /// 接受当前生成任务完成后的单张形象图片，拒绝过期或重复结果。
    /// </summary>
    public void CompleteImageGeneration(string generationId, string imagePath)
    {
        if (Status != "image_pending" || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("生成任务已失效，无法保存形象图片");
        }

        Candidates = new[] { imagePath };
        Status = "image_ready";
    }

    /// <summary>
    /// 当前形象图片生成任务失败后，记录原因并标记为失败。
    /// </summary>
    public void FailImageGeneration(string generationId, string errorMessage)
    {
        if (Status != "image_pending" || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("生成任务已失效，无法记录失败结果");
        }

        Status = "failed";
        Note = errorMessage;
    }

    /// <summary>
    /// 使用已上传的原画创建新作品，不触发图片生成。
    /// </summary>
    public static Work Create(string id, string owner, DateTimeOffset createdAt, string original)
    {
        return new Work(id, owner, createdAt, original);
    }
}
