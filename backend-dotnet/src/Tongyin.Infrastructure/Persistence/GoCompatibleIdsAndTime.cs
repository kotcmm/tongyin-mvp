using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;

namespace Tongyin.Infrastructure.Persistence;

// Uses the already-tested Go-compatible snapshot adapter while keeping all
// JSON and SQL details outside of Domain and Application.

/// <summary>
/// 生成与 Go 服务兼容的作品标识及时间。
/// </summary>
public sealed class GoCompatibleIdsAndTime : IWorkIdsAndTime
{
    /// <summary>
    /// 生成 Go 兼容的唯一标识。
    /// </summary>
    public string NewId()=>Doc.NewId();
    /// <summary>
    /// 获取当前时间。
    /// </summary>
    public string Now()=>Doc.Now();
}
