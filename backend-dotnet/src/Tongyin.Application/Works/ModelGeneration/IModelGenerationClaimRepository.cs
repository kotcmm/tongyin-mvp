namespace Tongyin.Application.Works;

/// <summary>原子认领模型生成任务的持久化端口，防止重复提交造成额外计费。</summary>
public interface IModelGenerationClaimRepository
{
    /// <summary>首次认领成功返回任务快照，错误阶段、旧编号及重复认领返回空。</summary>
    Task<WorkSnapshot?> ClaimAsync(string workId, string owner, string generationId, string providerId, int promptVersion, DateTimeOffset startedAt, CancellationToken cancellationToken = default);
}
