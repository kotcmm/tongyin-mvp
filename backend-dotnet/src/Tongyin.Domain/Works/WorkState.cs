namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品状态快照，承载跨层传递的只读数据。
/// </summary>
public sealed record WorkState(
    string Id, string Owner, string CreatedAt, string OriginalAsset,
    WorkStatus Status, int ImageAttempts, string GenerationId,
    IReadOnlyList<string> Candidates, int Selected, IReadOnlyList<string> Preview,
    string ModelAsset, string Note);
