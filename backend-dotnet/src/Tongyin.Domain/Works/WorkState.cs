namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品状态快照，承载跨层传递的只读数据。
/// </summary>
/// <param name="Id">业务参数</param>
/// <param name="Owner">业务参数</param>
/// <param name="CreatedAt">业务参数</param>
/// <param name="OriginalAsset">业务参数</param>
/// <param name="Status">业务参数</param>
/// <param name="ImageAttempts">业务参数</param>
/// <param name="GenerationId">业务参数</param>
/// <param name="Candidates">业务参数</param>
/// <param name="Selected">业务参数</param>
/// <param name="Preview">业务参数</param>
/// <param name="ModelAsset">业务参数</param>
/// <param name="Note">业务参数</param>
public sealed record WorkState(
    string Id, string Owner, string CreatedAt, string OriginalAsset,
    WorkStatus Status, int ImageAttempts, string GenerationId,
    IReadOnlyList<string> Candidates, int Selected, IReadOnlyList<string> Preview,
    string ModelAsset, string Note);
