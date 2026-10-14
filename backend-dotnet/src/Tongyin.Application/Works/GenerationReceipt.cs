using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Use-case input and output are independent of HTTP, files, database and legacy JSON.

/// <summary>
/// 生成任务受理结果，包含作品状态与是否启用自动生成。
/// </summary>
/// <param name="Work">生成任务关联作品的最新状态。</param>
/// <param name="Automatic">是否执行自动生成。</param>
public sealed record GenerationReceipt(WorkState Work,bool Automatic);
