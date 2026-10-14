using System.Data.Common;
using System.Text.Json.Nodes;
using MySqlConnector;
using Npgsql;

namespace Tongyin.Api.Core;

/// <summary>
/// 原有快照存储接口，仅供基础设施适配器使用。
/// </summary>
public interface IStore
{
    /// <summary>
    /// 读取当前存储数据快照。
    /// </summary>
    Task<T> Read<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
    /// <summary>
    /// 原子更新数据快照并保存结果。
    /// </summary>
    Task<T> Update<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
}
