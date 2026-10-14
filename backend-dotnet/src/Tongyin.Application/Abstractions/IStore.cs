namespace Tongyin.Api.Core;

// Application-facing persistence port. Implemented by infrastructure adapters.
public interface IStore
{
    Task<T> Read<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
    Task<T> Update<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
}
