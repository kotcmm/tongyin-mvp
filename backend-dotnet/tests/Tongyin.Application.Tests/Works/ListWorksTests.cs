using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>
/// 验证作品列表只读取当前微信用户的作品。
/// </summary>
public sealed class ListWorksTests
{
    /// <summary>
    /// 应用用例向持久化端口传递已认证的用户标识并返回读取结果。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyRepositoryOwnerResults()
    {
        var repository = new RecordingRepository();
        var useCase = new ListWorks(repository);

        var works = await useCase.ExecuteAsync("openid-001");

        Assert.Equal("openid-001", repository.RequestedOwner);
        Assert.Single(works);
        Assert.Equal("work-001", works[0].Id);
    }

    private sealed class RecordingRepository : IWorkRepository
    {
        public string? RequestedOwner { get; private set; }

        public Task AddAsync(Work work, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkSnapshot?> GetByIdForOwnerAsync(string id, string owner, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("列表用例测试不读取详情");

        public Task<IReadOnlyList<WorkSnapshot>> ListByOwnerAsync(string owner, CancellationToken cancellationToken = default)
        {
            RequestedOwner = owner;
            IReadOnlyList<WorkSnapshot> results = [new WorkSnapshot { Id = "work-001", Status = "image_pending" }];
            return Task.FromResult(results);
        }
    }
}
