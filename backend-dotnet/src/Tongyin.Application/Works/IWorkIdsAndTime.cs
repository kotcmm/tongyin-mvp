using Tongyin.Domain.Works;

namespace Tongyin.Application.Works;

// Aggregate persistence contract. Never exposes Go JSON or a database snapshot.

/// <summary>
/// 作品标识与时间提供接口。
/// </summary>
public interface IWorkIdsAndTime
{
    /// <summary>
    /// 生成新的业务标识。
    /// </summary>
    string NewId();
    /// <summary>
    /// 取得当前时间。
    /// </summary>
    string Now();
}
