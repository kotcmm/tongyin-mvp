namespace Tongyin.Application.Works;

/// <summary>等待派发到 3D worker 的作品任务身份信息。</summary>
public sealed class QueuedModelGeneration
{
    /// <summary>作品编号。</summary>
    public required string WorkId { get; init; }

    /// <summary>微信用户 OpenID。</summary>
    public required string Owner { get; init; }

    /// <summary>当前生成任务编号。</summary>
    public required string GenerationId { get; init; }
}
