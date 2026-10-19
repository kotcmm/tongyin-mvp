namespace Tongyin.Application.Works;

/// <summary>作品删除用例，所有权、订单关联和生成任务安全由同一数据库事务保障。</summary>
public sealed class DeleteWork(IWorkDeletionRepository repository)
{
    /// <summary>申请删除用户本人作品并返回可解释的业务结果。</summary>
    public Task<DeleteWorkResult> ExecuteAsync(string owner, string id, CancellationToken cancellationToken = default)
        => repository.DeleteWorkAsync(id, owner, cancellationToken);
}
