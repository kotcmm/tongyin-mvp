using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

// Uses the already-tested Go-compatible snapshot adapter while keeping all
// JSON and SQL details outside of Domain and Application.

/// <summary>
/// 以 Go 存储格式实现作品聚合仓储接口。
/// </summary>
public sealed class LegacyWorkRepository(IStore store) : IWorkRepository
{
    /// <summary>
    /// 持久化新增作品聚合。
    /// </summary>
    public Task AddAsync(Work work,CancellationToken ct=default) => store.Update(s=>
    {
        if(s.Works.ContainsKey(work.Id))
            throw new InvalidOperationException("作品编号重复");
        s.Works[work.Id]=LegacyWorkDocument.ToDocument(work.Snapshot());
        return true;
    },ct);

    /// <summary>
    /// 按用户标识查找聚合。
    /// </summary>
    public Task<Work?> FindAsync(string id,string owner,CancellationToken ct=default) =>
        store.Read(s=>s.Works.TryGetValue(id,out var doc) && Doc.Str(doc,"owner")==owner
            ? LegacyWorkDocument.ToDomain(doc) : null,ct);

    /// <summary>
    /// 按用户标识读取作品集合。
    /// </summary>
    public async Task<IReadOnlyList<Work>> ListAsync(string owner,CancellationToken ct=default) =>
        await store.Read<IReadOnlyList<Work>>(s=>s.Works.Values
            .Where(w=>Doc.Str(w,"owner")==owner)
            .OrderByDescending(w=>Doc.Str(w,"created_at"))
            .Select(LegacyWorkDocument.ToDomain).ToArray(),ct);

    /// <summary>
    /// 加载聚合、执行修改并原子保存。
    /// </summary>
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
