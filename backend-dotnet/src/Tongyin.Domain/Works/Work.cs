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
    public WorkStatus Status { get; private set; } = WorkStatus.ImagePending;

    /// <summary>
    /// 已选择的候选图索引，负一表示尚未选择。
    /// </summary>
    public int Selected { get; private set; } = -1;

    /// <summary>
    /// 已消耗的图片生成次数。
    /// </summary>
    public int ImageAttempts { get; private set; }

    /// <summary>
    /// 已生成的候选图路径。
    /// </summary>
    public IReadOnlyList<string> Candidates { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 已生成的三维模型 GLB 文件存储路径，未生成时为空。
    /// </summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>
    /// 模型预览图路径。
    /// </summary>
    public IReadOnlyList<string> Preview { get; } = Array.Empty<string>();

    /// <summary>
    /// 当前阶段的生成失败原因，初始为空。
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
        if (Status != WorkStatus.ImageReady && Status != WorkStatus.ImagePending
            && !(Status == WorkStatus.Failed && Selected < 0))
        {
            throw new InvalidOperationException("当前作品不能重新生成形象");
        }

        if (Status == WorkStatus.ImagePending && ImageAttempts > 0)
        {
            throw new InvalidOperationException("形象正在生成，请勿重复提交");
        }

        if (ImageAttempts >= freeImageAttempts)
        {
            throw new InvalidOperationException("免费生成次数已用完");
        }

        ImageAttempts++;
        GenerationId = generationId;
        Status = WorkStatus.ImagePending;
        Candidates = Array.Empty<string>();
        Note = string.Empty;
    }

    /// <summary>
    /// 接受当前生成任务完成后的单张形象图片，拒绝过期或重复结果。
    /// </summary>
    public void CompleteImageGeneration(string generationId, string imagePath)
    {
        if (Status != WorkStatus.ImagePending || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("生成任务已失效，无法保存形象图片");
        }

        Candidates = new[] { imagePath };
        Status = WorkStatus.ImageReady;
    }

    /// <summary>
    /// 当前形象图片生成任务失败后，记录原因并标记为失败。
    /// </summary>
    public void FailImageGeneration(string generationId, string errorMessage)
    {
        if (Status != WorkStatus.ImagePending || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("生成任务已失效，无法记录失败结果");
        }

        Status = WorkStatus.Failed;
        Note = errorMessage;
    }

    /// <summary>
    /// 用户确认唯一的候选图片，进入三维模型待生成状态。
    /// </summary>
    public void SelectImage(int index)
    {
        if (Status != WorkStatus.ImageReady || index != 0)
        {
            throw new InvalidOperationException("无法选择此图片");
        }

        Selected = index;
        Status = WorkStatus.ModelPending;
    }

    /// <summary>
    /// 当前三维模型生成任务完成后保存 GLB 文件路径，并进入模型就绪状态。
    /// </summary>
    public void CompleteModelGeneration(string generationId, string modelPath)
    {
        if (Status != WorkStatus.ModelPending || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("模型生成任务已失效，无法保存结果");
        }

        if (string.IsNullOrEmpty(modelPath))
        {
            throw new ArgumentException("模型文件路径不能为空", nameof(modelPath));
        }

        Model = modelPath;
        Status = WorkStatus.ModelReady;
    }

    /// <summary>
    /// 当前三维模型生成任务失败时记录原因，并保留用户已经确认的图片。
    /// </summary>
    public void FailModelGeneration(string generationId, string errorMessage)
    {
        if (Status != WorkStatus.ModelPending || GenerationId.Length == 0 || GenerationId != generationId)
        {
            throw new InvalidOperationException("模型生成任务已失效，无法记录失败结果");
        }

        Status = WorkStatus.Failed;
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
