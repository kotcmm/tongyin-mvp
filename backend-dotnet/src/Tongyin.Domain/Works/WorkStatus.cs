namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品生命周期状态。
/// </summary>
public enum WorkStatus
{
    /// <summary>
    /// 等待卡通形象生成。
    /// </summary>
    ImagePending,
    /// <summary>
    /// 卡通形象已经生成。
    /// </summary>
    ImageReady,
    /// <summary>
    /// 等待三维模型生成。
    /// </summary>
    ModelPending,
    /// <summary>
    /// 三维模型已经准备就绪。
    /// </summary>
    ModelReady,
    /// <summary>
    /// 生成失败。
    /// </summary>
    Failed,
    /// <summary>
    /// 未知作品状态。
    /// </summary>
    Unknown
}
