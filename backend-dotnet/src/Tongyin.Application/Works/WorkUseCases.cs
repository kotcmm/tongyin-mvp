using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Use-case input and output are independent of HTTP, files, database and legacy JSON.
public sealed record GenerationReceipt(WorkState Work,bool Automatic);

public sealed class WorkUseCases(IWorkRepository works,IWorkGenerationGateway generation,IWorkIdsAndTime ids)
{
    public async Task<WorkState> CreateAsync(string owner,string storedAsset,CancellationToken ct=default)
    {
        var work=Work.Create(ids.NewId(),owner,storedAsset,ids.Now());
        await works.AddAsync(work,ct);
        return work.Snapshot();
    }

    public async Task<IReadOnlyList<WorkState>> ListAsync(string owner,CancellationToken ct=default) =>
        (await works.ListAsync(owner,ct)).Select(w=>w.Snapshot()).ToArray();

    public async Task<WorkState?> GetAsync(string owner,string id,CancellationToken ct=default) =>
        (await works.FindAsync(id,owner,ct))?.Snapshot();

    public async Task<GenerationReceipt> GenerateAsync(string owner,string id,int freeAttempts,CancellationToken ct=default)
    {
        var updated=await works.UpdateAsync(id,owner,w=>w.RequestImage(freeAttempts,ids.NewId()),ct);
        var state=updated.Snapshot();
        generation.Dispatch(state,"image");
        return new(state,await generation.IsEnabledAsync("image"));
    }

    public async Task<GenerationReceipt> SelectAsync(string owner,string id,int index,CancellationToken ct=default)
    {
        var updated=await works.UpdateAsync(id,owner,w=>w.SelectFirstImage(index),ct);
        var state=updated.Snapshot();
        generation.Dispatch(state,"model");
        return new(state,await generation.IsEnabledAsync("model"));
    }
}
