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
    public string Status { get; } = "image_pending";

    /// <summary>
    /// 已选择的候选图索引，负一表示尚未选择。
    /// </summary>
    public int Selected { get; } = -1;

    /// <summary>
    /// 已消耗的图片生成次数。
    /// </summary>
    public int ImageAttempts { get; }

    /// <summary>
    /// 已生成的候选图路径。
    /// </summary>
    public IReadOnlyList<string> Candidates { get; } = Array.Empty<string>();

    /// <summary>
    /// 模型预览图路径。
    /// </summary>
    public IReadOnlyList<string> Preview { get; } = Array.Empty<string>();

    /// <summary>
    /// 使用已上传的原画创建新作品，不触发图片生成。
    /// </summary>
    public static Work Create(string id, string owner, DateTimeOffset createdAt, string original)
    {
        return new Work(id, owner, createdAt, original);
    }
}
