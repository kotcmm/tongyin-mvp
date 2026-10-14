using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Aggregate persistence contract. Never exposes Go JSON or a database snapshot.

/// <summary>
/// 作品生成网关接口，屏蔽外部图像和三维模型生成服务。
/// </summary>
public interface IWorkGenerationGateway
{
    /// <summary>
    /// 提交作品的图像或模型生成任务。
    /// </summary>
    void Dispatch(WorkState work,string stage);
    /// <summary>
    /// 查询指定生成阶段是否已启用自动生成。
    /// </summary>
    Task<bool> IsEnabledAsync(string stage);
}
