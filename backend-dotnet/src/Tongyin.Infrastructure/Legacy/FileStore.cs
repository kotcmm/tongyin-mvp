using System.Data.Common;
using System.Text.Json.Nodes;
using MySqlConnector;
using Npgsql;

namespace Tongyin.Api.Core;

/// <summary>
/// 本地 JSON 文件存储实现。
/// </summary>
public sealed class FileStore(string path) : IStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot Load() => File.Exists(path) ? Snapshot.FromJson(Doc.Parse(File.ReadAllText(path))) : new();
    public async Task<T> Read<T>(Func<Snapshot,T> fn, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return fn(Load()); } finally { _gate.Release(); }
    }
    public async Task<T> Update<T>(Func<Snapshot,T> fn, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var snap = Load();
            var result = fn(snap);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Doc.NewId() + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, snap.ToJson().ToJsonString(), ct);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return result;
        }
        finally { _gate.Release(); }
    }
}
