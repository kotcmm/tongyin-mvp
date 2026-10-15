namespace Tongyin.Domain.Works;

/// <summary>
/// 儿童画作品的处理状态。
/// </summary>
public enum WorkStatus
{
    /// <summary>
    /// 等待生成形象图片。
    /// </summary>
    ImagePending = 0,

    /// <summary>
    /// 形象图片已生成，可供用户确认。
    /// </summary>
    ImageReady = 1,

    /// <summary>
    /// 等待生成三维模型。
    /// </summary>
    ModelPending = 2,

    /// <summary>
    /// 三维模型已生成完成。
    /// </summary>
    ModelReady = 3,

    /// <summary>
    /// 当前阶段处理失败。
    /// </summary>
    Failed = 4
}
