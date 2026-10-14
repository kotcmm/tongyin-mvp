namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品聚合根，负责形象生成与选图的状态约束。
/// </summary>
public sealed class Work
{
    private readonly List<string> _candidates;
    private readonly List<string> _preview;
    private Work(WorkState state)
    {
        if(string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.Owner))
            throw new ArgumentException("作品标识或用户无效");
        Id=state.Id; Owner=state.Owner; CreatedAt=state.CreatedAt; OriginalAsset=state.OriginalAsset;
        Status=state.Status; ImageAttempts=state.ImageAttempts; GenerationId=state.GenerationId;
        _candidates=state.Candidates.ToList(); Selected=state.Selected; _preview=state.Preview.ToList();
        ModelAsset=state.ModelAsset; Note=state.Note;
    }

    /// <summary>
    /// 唯一标识。
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// 所属用户标识。
    /// </summary>
    public string Owner { get; }
    /// <summary>
    /// 创建时间。
    /// </summary>
    public string CreatedAt { get; }
    /// <summary>
    /// 儿童原画资源标识。
    /// </summary>
    public string OriginalAsset { get; }
    /// <summary>
    /// 当前业务状态。
    /// </summary>
    public WorkStatus Status { get; private set; }
    /// <summary>
    /// 形象生成已尝试次数。
    /// </summary>
    public int ImageAttempts { get; private set; }
    /// <summary>
    /// 本次生成任务标识。
    /// </summary>
    public string GenerationId { get; private set; }
    /// <summary>
    /// 已确认的形象序号。
    /// </summary>
    public int Selected { get; private set; }
    /// <summary>
    /// 三维模型资源标识。
    /// </summary>
    public string ModelAsset { get; private set; }
    /// <summary>
    /// 处理备注。
    /// </summary>
    public string Note { get; private set; }
    /// <summary>
    /// 提供AsReadOnly对应的业务操作。
    /// </summary>
    public IReadOnlyList<string> Candidates => _candidates.AsReadOnly();

    /// <summary>
    /// 创建新的业务对象。
    /// </summary>
    public static Work Create(string id,string owner,string original,string createdAt) =>
        new(new WorkState(id,owner,createdAt,original,WorkStatus.ImagePending,0,"",
            Array.Empty<string>(),-1,Array.Empty<string>(),"",""));

    /// <summary>
    /// 根据已存储状态还原业务对象。
    /// </summary>
    public static Work Restore(WorkState state) => new(state);

    /// <summary>
    /// 取得独立的业务状态快照。
    /// </summary>
    public WorkState Snapshot() => new(Id,Owner,CreatedAt,OriginalAsset,Status,
        ImageAttempts,GenerationId,_candidates.ToArray(),Selected,_preview.ToArray(),ModelAsset,Note);

    /// <summary>
    /// 校验形象生成条件并记录新的生成任务。
    /// </summary>
    public void RequestImage(int freeAttempts,string generationId)
    {
        if(Status is not (WorkStatus.ImageReady or WorkStatus.ImagePending) &&
           !(Status==WorkStatus.Failed && Selected<0))
            throw new InvalidOperationException("当前作品不能重新生成形象");
        if(Status==WorkStatus.ImagePending && ImageAttempts>0)
            throw new InvalidOperationException("形象正在生成，请勿重复提交");
        if(ImageAttempts>=freeAttempts)
            throw new InvalidOperationException("免费生成次数已用完");
        ImageAttempts++;
        GenerationId=generationId;
        Status=WorkStatus.ImagePending;
        Selected=-1;
        _candidates.Clear(); _preview.Clear(); ModelAsset=""; Note="";
    }

    /// <summary>
    /// 确认第一张形象并进入三维模型生成阶段。
    /// </summary>
    public void SelectFirstImage(int index)
    {
        if(Status!=WorkStatus.ImageReady || index!=0 || _candidates.Count==0)
            throw new ArgumentException("无法选择此图片");
        var first=_candidates[0];
        _candidates.Clear(); _candidates.Add(first);
        Selected=0;
        _preview.Clear();
        ModelAsset="";
        Status=WorkStatus.ModelPending;
    }
}
