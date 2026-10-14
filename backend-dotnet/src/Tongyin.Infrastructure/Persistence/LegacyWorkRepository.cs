using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

// Uses the already-tested Go-compatible snapshot adapter while keeping all
// JSON and SQL details outside of Domain and Application.
public sealed class LegacyWorkRepository(IStore store) : IWorkRepository
{
    public Task AddAsync(Work work,CancellationToken ct=default) => store.Update(s=>
    {
        if(s.Works.ContainsKey(work.Id))
            throw new InvalidOperationException("作品编号重复");
        s.Works[work.Id]=LegacyWorkDocument.ToDocument(work.Snapshot());
        return true;
    },ct);

    public Task<Work?> FindAsync(string id,string owner,CancellationToken ct=default) =>
        store.Read(s=>s.Works.TryGetValue(id,out var doc) && Doc.Str(doc,"owner")==owner
            ? LegacyWorkDocument.ToDomain(doc) : null,ct);

    public async Task<IReadOnlyList<Work>> ListAsync(string owner,CancellationToken ct=default) =>
        await store.Read<IReadOnlyList<Work>>(s=>s.Works.Values
            .Where(w=>Doc.Str(w,"owner")==owner)
            .OrderByDescending(w=>Doc.Str(w,"created_at"))
            .Select(LegacyWorkDocument.ToDomain).ToArray(),ct);

    public Task<Work> UpdateAsync(string id,string owner,Action<Work> mutation,CancellationToken ct=default) =>
        store.Update(s=>
        {
            if(!s.Works.TryGetValue(id,out var doc) || Doc.Str(doc,"owner")!=owner)
                throw new KeyNotFoundException("作品不存在");
            var aggregate=LegacyWorkDocument.ToDomain(doc);
            mutation(aggregate);
            s.Works[id]=LegacyWorkDocument.ToDocument(aggregate.Snapshot(),doc);
            return aggregate;
        },ct);
}

public sealed class GoCompatibleIdsAndTime : IWorkIdsAndTime
{
    public string NewId()=>Doc.NewId();
    public string Now()=>Doc.Now();
}
