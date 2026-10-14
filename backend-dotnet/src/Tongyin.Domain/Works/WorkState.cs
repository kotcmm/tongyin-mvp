namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品状态快照，承载跨层传递的只读数据。
/// </summary>
/// <param name="Id">作品编号</param>
/// <param name="Owner">所属客户标识</param>
/// <param name="CreatedAt">作品创建时间</param>
/// <param name="OriginalAsset">儿童原画文件名</param>
/// <param name="Status">当前作品状态</param>
/// <param name="ImageAttempts">图片生成次数</param>
/// <param name="GenerationId">生成任务编号</param>
/// <param name="Candidates">候选形象文件列表</param>
/// <param name="Selected">已确认的形象序号</param>
/// <param name="Preview">预览资源列表</param>
/// <param name="ModelAsset">三维模型文件名</param>
/// <param name="Note">处理说明</param>
public sealed record WorkState(string Id, string Owner, string CreatedAt, string OriginalAsset, WorkStatus Status, int ImageAttempts, string GenerationId, IReadOnlyList<string> Candidates, int Selected, IReadOnlyList<string> Preview, string ModelAsset, string Note);
