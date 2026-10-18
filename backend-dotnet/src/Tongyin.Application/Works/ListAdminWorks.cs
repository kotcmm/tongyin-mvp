namespace Tongyin.Application.Works;

/// <summary>提供受管理员认证保护的全体作品只读查询用例。</summary>
public sealed class ListAdminWorks(IAdminWorksReader reader)
{
    /// <summary>读取运营人员需要核对的原画、生成图片、模型与业务状态。</summary>
    public Task<IReadOnlyList<WorkSnapshot>> ExecuteAsync(CancellationToken cancellationToken = default)
        => reader.ListAsync(cancellationToken);
}
