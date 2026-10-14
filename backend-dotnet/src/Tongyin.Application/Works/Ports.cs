using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Aggregate persistence contract. Never exposes Go JSON or a database snapshot.
public interface IWorkRepository
{
    Task AddAsync(Work work,CancellationToken cancellationToken=default);
    Task<Work?> FindAsync(string id,string owner,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<Work>> ListAsync(string owner,CancellationToken cancellationToken=default);
    Task<Work> UpdateAsync(string id,string owner,Action<Work> mutation,CancellationToken cancellationToken=default);
}

public interface IWorkGenerationGateway
{
    void Dispatch(WorkState work,string stage);
    Task<bool> IsEnabledAsync(string stage);
}

public interface IWorkIdsAndTime
{
    string NewId();
    string Now();
}
