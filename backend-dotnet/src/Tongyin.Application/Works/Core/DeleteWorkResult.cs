namespace Tongyin.Application.Works;

/// <summary>作品删除的明确业务结果，用于 HTTP 层区分权限、订单关联及 AI 任务状态。</summary>
public enum DeleteWorkResult
{
    /// <summary>作品已从用户作品清单中移除。</summary>
    Deleted,
    /// <summary>没有找到属于当前用户的作品。</summary>
    NotFound,
    /// <summary>作品已有历史订单，不能破坏订单生产与售后关联。</summary>
    HasOrders,
    /// <summary>AI 任务可能已提交或正在执行，删除存在计费及回写风险。</summary>
    Processing
}
