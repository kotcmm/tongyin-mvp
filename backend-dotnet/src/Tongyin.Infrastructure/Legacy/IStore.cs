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
    Task<T> Read<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
    Task<T> Update<T>(Func<Snapshot,T> fn, CancellationToken ct = default);
}
