using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Use-case input and output are independent of HTTP, files, database and legacy JSON.

/// <summary>
/// 作品应用用例，协调仓储与生成网关。
/// </summary>
public sealed class WorkUseCases(IWorkRepository works,IWorkGenerationGateway generation,IWorkIdsAndTime ids)
{
    /// <summary>
    /// 创建作品并写入作品仓储。
    /// </summary>
    public async Task<WorkState> CreateAsync(string owner,string storedAsset,CancellationToken ct=default)
    {
        var work=Work.Create(ids.NewId(),owner,storedAsset,ids.Now());
        await works.AddAsync(work,ct);
        return work.Snapshot();
    }

    /// <summary>
    /// 查询当前用户的作品列表。
    /// </summary>
    public async Task<IReadOnlyList<WorkState>> ListAsync(string owner,CancellationToken ct=default) =>
        (await works.ListAsync(owner,ct)).Select(w=>w.Snapshot()).ToArray();

    /// <summary>
    /// 查询当前用户的指定作品。
    /// </summary>
    public async Task<WorkState?> GetAsync(string owner,string id,CancellationToken ct=default) =>
        (await works.FindAsync(id,owner,ct))?.Snapshot();

    /// <summary>
    /// 申请生成卡通形象并派发任务。
    /// </summary>
    public async Task<GenerationReceipt> GenerateAsync(string owner,string id,int freeAttempts,CancellationToken ct=default)
    {
        var updated=await works.UpdateAsync(id,owner,w=>w.RequestImage(freeAttempts,ids.NewId()),ct);
        var state=updated.Snapshot();
        generation.Dispatch(state,"image");
        return new(state,await generation.IsEnabledAsync("image"));
    }

    /// <summary>
    /// 确认生成形象并派发三维模型任务。
    /// </summary>
    public async Task<GenerationReceipt> SelectAsync(string owner,string id,int index,CancellationToken ct=default)
    {
        var updated=await works.UpdateAsync(id,owner,w=>w.SelectFirstImage(index),ct);
        var state=updated.Snapshot();
        generation.Dispatch(state,"model");
        return new(state,await generation.IsEnabledAsync("model"));
    }
}
