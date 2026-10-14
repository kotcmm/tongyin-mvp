using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Use-case input and output are independent of HTTP, files, database and legacy JSON.

/// <summary>
/// 生成任务受理结果，包含作品状态与是否启用自动生成。
/// </summary>
/// <param name="Work">业务参数</param>
/// <param name="Automatic">业务参数</param>
public sealed record GenerationReceipt(WorkState Work,bool Automatic);
