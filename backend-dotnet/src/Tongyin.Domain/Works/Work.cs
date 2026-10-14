namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.
public enum WorkStatus { ImagePending, ImageReady, ModelPending, ModelReady, Failed, Unknown }

public sealed record WorkState(
    string Id, string Owner, string CreatedAt, string OriginalAsset,
    WorkStatus Status, int ImageAttempts, string GenerationId,
    IReadOnlyList<string> Candidates, int Selected, IReadOnlyList<string> Preview,
    string ModelAsset, string Note);

public sealed class Work
{
    private readonly List<string> candidates;
    private readonly List<string> preview;
    private Work(WorkState state)
    {
        if(string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.Owner))
            throw new ArgumentException("作品标识或用户无效");
        Id=state.Id; Owner=state.Owner; CreatedAt=state.CreatedAt; OriginalAsset=state.OriginalAsset;
        Status=state.Status; ImageAttempts=state.ImageAttempts; GenerationId=state.GenerationId;
        candidates=state.Candidates.ToList(); Selected=state.Selected; preview=state.Preview.ToList();
        ModelAsset=state.ModelAsset; Note=state.Note;
    }

    public string Id { get; }
    public string Owner { get; }
    public string CreatedAt { get; }
    public string OriginalAsset { get; }
    public WorkStatus Status { get; private set; }
    public int ImageAttempts { get; private set; }
    public string GenerationId { get; private set; }
    public int Selected { get; private set; }
    public string ModelAsset { get; private set; }
    public string Note { get; private set; }
    public IReadOnlyList<string> Candidates => candidates.AsReadOnly();

    public static Work Create(string id,string owner,string original,string createdAt) =>
        new(new WorkState(id,owner,createdAt,original,WorkStatus.ImagePending,0,"",
            Array.Empty<string>(),-1,Array.Empty<string>(),"",""));

    public static Work Restore(WorkState state) => new(state);

    public WorkState Snapshot() => new(Id,Owner,CreatedAt,OriginalAsset,Status,
        ImageAttempts,GenerationId,candidates.ToArray(),Selected,preview.ToArray(),ModelAsset,Note);

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
        candidates.Clear(); preview.Clear(); ModelAsset=""; Note="";
    }

    public void SelectFirstImage(int index)
    {
        if(Status!=WorkStatus.ImageReady || index!=0 || candidates.Count==0)
            throw new ArgumentException("无法选择此图片");
        var first=candidates[0];
        candidates.Clear(); candidates.Add(first);
        Selected=0;
        preview.Clear();
        ModelAsset="";
        Status=WorkStatus.ModelPending;
    }
}
