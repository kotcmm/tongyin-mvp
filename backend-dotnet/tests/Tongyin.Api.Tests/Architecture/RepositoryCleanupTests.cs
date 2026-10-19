using Xunit;

namespace Tongyin.Api.Tests.Architecture;

/// <summary>确保已切换到 .NET 的仓库不再混入 Go 运行时代码、旧后台和旧迁移脚本。</summary>
public sealed class RepositoryCleanupTests
{
    /// <summary>仓库只有 .NET API、当前运营后台与微信小程序，不保留可误部署的旧 Go 入口。</summary>
    [Fact]
    public void OldGoRuntimeAndAdminAssetsMustNotRemain()
    {
        var root = FindRepositoryRoot();
        foreach (var path in new[]
        {
            "backend", "admin", "migrations", "docs", "Dockerfile", "compose.yaml",
            ".dockerignore", ".github/workflows/backend-test.yml",
            "scripts/dev.ps1", "scripts/check-dev-phone.cjs",
            "scripts/check-admin-search.cjs", "scripts/check-operations.cjs",
            "scripts/check-refactor-layout.cjs", "backend-dotnet/tests/test_architecture.py"
        })
        {
            Assert.False(File.Exists(Path.Combine(root, path)) || Directory.Exists(Path.Combine(root, path)),
                $"旧实现必须清理：{path}");
        }
        Assert.Empty(Directory.GetFiles(root, "*.go", SearchOption.AllDirectories));
    }

    /// <summary>独立 .NET 迁移文件必须包含基础业务、系统设置与 Identity，禁止保留已废弃的凭据建表脚本。</summary>
    [Fact]
    public void DatabaseMigrationsKeepOnlyActiveSchemaAndExplicitLegacyCleanup()
    {
        var root = FindRepositoryRoot();
        foreach (var engine in new[] { "postgres", "mysql" })
        {
            var folder = Path.Combine(root, "backend-dotnet", "sql", engine);
            foreach (var file in new[] { "000_baseline.sql", "001_system_settings.sql", "003_identity_admin.sql", "004_media_cleanup.sql", "005_model_jobs.sql", "006_reset_ai_provider_credentials.sql" })
            {
                Assert.True(File.Exists(Path.Combine(folder, file)), $"缺少数据库迁移：{engine}/{file}");
            }
            Assert.False(File.Exists(Path.Combine(folder, "002_admin_credentials.sql")));
        }

        Assert.True(File.Exists(Path.Combine(root, "backend-dotnet", "sql", "postgres", "900_drop_legacy_admin_tables.sql")));
    }

    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "backend-dotnet", "Tongyin.slnx"))
                && Directory.Exists(Path.Combine(folder.FullName, "miniprogram")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("未找到童印仓库根目录");
    }
}
