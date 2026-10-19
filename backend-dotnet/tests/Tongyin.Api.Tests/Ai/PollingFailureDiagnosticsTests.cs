using System.Net.Sockets;
using Npgsql;
using Tongyin.Api.Hosting;
using Xunit;

namespace Tongyin.Api.Tests.Ai;

/// <summary>本地调试中 AI 轮询的数据库失败必须给出不泄密的诊断，并避免高频重复输出。</summary>
public sealed class PollingFailureDiagnosticsTests
{
    /// <summary>端口拒绝连接时应提示检查 PostgreSQL 服务或端口，不能暴露连接字符串内容。</summary>
    [Fact]
    public void Describe_ConnectionRefused_ExplainsPostgresqlReachabilityWithoutSecrets()
    {
        var exception = new NpgsqlException(
            "Failed: Password=do-not-print",
            new SocketException((int)SocketError.ConnectionRefused));

        var description = PollingFailureDiagnostics.Describe(exception);

        Assert.Contains("连接被拒绝", description);
        Assert.Contains("PostgreSQL", description);
        Assert.DoesNotContain("do-not-print", description);
        Assert.DoesNotContain("Password=", description);
    }

    /// <summary>未知 Npgsql 错误只说明检查连接和日志，不拼接未经脱敏的原始异常消息。</summary>
    [Fact]
    public void Describe_GenericDatabaseFailure_DoesNotEchoExceptionMessage()
    {
        var description = PollingFailureDiagnostics.Describe(new NpgsqlException("API Key password=leak"));

        Assert.Contains("PostgreSQL", description);
        Assert.DoesNotContain("leak", description);
        Assert.DoesNotContain("password", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>连续相同的后台轮询错误一分钟内只输出首次，避免每五秒刷满 VS 输出。</summary>
    [Fact]
    public void ShouldLog_RepeatedFailure_ThrottlesUntilOneMinute()
    {
        var diagnostics = new PollingFailureDiagnostics();
        var now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

        Assert.True(diagnostics.ShouldLog("postgres-refused", now));
        Assert.False(diagnostics.ShouldLog("postgres-refused", now.AddSeconds(5)));
        Assert.True(diagnostics.ShouldLog("postgres-refused", now.AddMinutes(1)));
    }

    /// <summary>错误类型变化需立即输出新原因；成功后再次出错也必须及时告警。</summary>
    [Fact]
    public void ShouldLog_ChangedErrorOrRecoveredState_ReportsImmediately()
    {
        var diagnostics = new PollingFailureDiagnostics();
        var now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

        Assert.True(diagnostics.ShouldLog("postgres-refused", now));
        Assert.True(diagnostics.ShouldLog("postgres-auth", now.AddSeconds(5)));
        diagnostics.Reset();
        Assert.True(diagnostics.ShouldLog("postgres-auth", now.AddSeconds(10)));
    }
}
