namespace Tongyin.Application.Works;

/// <summary>
/// 由 .NET 登记、尚未认领的一笔图片生成任务内部派发信息，不对微信小程序公开。
/// </summary>
public sealed class QueuedImageGeneration
{
    /// <summary>作品标识。</summary>
    public required string WorkId { get; init; }

    /// <summary>数据库 owner_id 中的实际微信用户标识。</summary>
    public required string Owner { get; init; }

    /// <summary>原子登记的当前图片生成任务编号。</summary>
    public required string GenerationId { get; init; }
}
