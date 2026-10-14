using Microsoft.AspNetCore.StaticFiles;

namespace Tongyin.Api.Core;

/// <summary>
/// 提供管理后台静态文件及三维模型预览资源，并防止越权目录访问。
/// </summary>
public sealed class AdminAssetServer
{
    private readonly string[] _roots;
    private readonly FileExtensionContentTypeProvider _mime = new();

    private AdminAssetServer(string[] roots)
    {
        _roots = roots;
        _mime.Mappings[".mjs"] = "text/javascript";
        _mime.Mappings[".js"] = "text/javascript";
        _mime.Mappings[".glb"] = "model/gltf-binary";
    }

    /// <summary>
    /// 查找可用管理后台静态资源目录。
    /// </summary>
    public static AdminAssetServer Discover(string contentRoot)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            var absolute = Path.GetFullPath(path);
            if (Directory.Exists(absolute) && seen.Add(absolute))
                roots.Add(absolute);
        }

        // In a published application all assets are copied under wwwroot/admin.
        Add(Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin"));

        // During dotnet run / TestServer, repository sources are also available.
        // Search from all three anchors because TestServer may override ContentRoot.
        foreach (var anchor in new[] { contentRoot, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (!Directory.Exists(anchor))
                continue;

            for (var current = new DirectoryInfo(anchor); current != null; current = current.Parent)
            {
                Add(Path.Combine(current.FullName, "wwwroot", "admin"));
                Add(Path.Combine(current.FullName, "backend", "webadmin"));
                Add(Path.Combine(current.FullName, "admin"));
            }
        }

        if (!roots.Any(root => File.Exists(Path.Combine(root, "index.html"))))
            throw new InvalidOperationException(
                "未找到童印管理后台 index.html。请从完整仓库运行，或检查 .NET 发布目录 wwwroot/admin 的静态资源复制配置。");

        return new AdminAssetServer(roots.ToArray());
    }

    /// <summary>
    /// 验证文件路径并返回静态或签名媒体资源。
    /// </summary>
    public IResult Serve(string? requested)
    {
        if (string.IsNullOrEmpty(requested))
            requested = "index.html";
        if (requested.EndsWith('/'))
            requested += "index.html";

        // Any traversal, alternate separator, or platform-specific path is denied.
        if (requested.Contains('\\') || requested.Contains(':') ||
            requested.Any(c => c == 0) || requested.StartsWith('/') ||
            requested.Split('/').Any(segment => segment is "" or "." or ".."))
            return Results.NotFound();

        foreach (var root in _roots)
        {
            var full = Path.GetFullPath(Path.Combine(root, requested.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;

            if (!File.Exists(full))
                continue;

            if (!_mime.TryGetContentType(full, out var contentType))
                contentType = "application/octet-stream";

            return Results.File(full, contentType, enableRangeProcessing: false);
        }

        return Results.NotFound();
    }
}
