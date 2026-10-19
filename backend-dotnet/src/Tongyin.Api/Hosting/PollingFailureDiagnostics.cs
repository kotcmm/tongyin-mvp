using System.Net.Sockets;
using Npgsql;

namespace Tongyin.Api.Hosting;

/// <summary>轮询异常的安全分类与重复日志限频，避免打印含数据库凭据或 AI 密钥的原始异常。</summary>
public sealed class PollingFailureDiagnostics
{
    private string? _lastReason;
    private DateTimeOffset _lastLoggedAt;

    /// <summary>仅在首次失败、失败类别变化或距上次提示满一分钟时返回 true。</summary>
    public bool ShouldLog(string reason, DateTimeOffset now)
    {
        if (_lastReason == reason && now - _lastLoggedAt < TimeSpan.FromMinutes(1))
        {
            return false;
        }

        _lastReason = reason;
        _lastLoggedAt = now;
        return true;
    }

    /// <summary>完成一次正常查询后重置状态，使后续新故障立即可见。</summary>
    public void Reset() => _lastReason = null;

    /// <summary>将数据库异常转换为固定且不含外部响应正文的排查提示。</summary>
    public static string Describe(Exception error)
    {
        if (error is PostgresException postgres)
        {
            return postgres.SqlState switch
            {
                "28P01" => "PostgreSQL 账号或密码验证失败，请检查 DB_CONNECTION_STRING",
                "3D000" => "PostgreSQL 目标数据库不存在，请检查 Database 名称",
                "42P01" => "PostgreSQL 业务表不存在，请检查童印业务库建表和迁移",
                "42501" => "PostgreSQL 数据库权限不足，请检查当前账号访问权限",
                _ => "PostgreSQL 查询失败，请检查数据库权限、业务表及迁移"
            };
        }

        if (error is NpgsqlException)
        {
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            {
                if (cause is SocketException socket)
                {
                    return socket.SocketErrorCode switch
                    {
                        SocketError.ConnectionRefused => "PostgreSQL 连接被拒绝，请确认服务已启动及端口是否正确",
                        SocketError.TimedOut => "PostgreSQL 连接超时，请检查地址、端口或网络防火墙",
                        SocketError.HostNotFound => "PostgreSQL 主机名无法解析，请检查 Host 配置",
                        _ => "PostgreSQL 网络连接失败，请检查数据库服务、Host 与端口"
                    };
                }
            }

            return "PostgreSQL 连接或通信失败，请确认本机服务和 DB_CONNECTION_STRING";
        }

        return "任务派发过程异常，请检查服务配置和关联业务状态";
    }
}
