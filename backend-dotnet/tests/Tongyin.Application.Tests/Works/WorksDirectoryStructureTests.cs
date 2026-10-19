using Tongyin.Application.Works;
using Xunit;

namespace Tongyin.Application.Tests.Works;

/// <summary>约束作品应用层按业务职责组织文件，同时保持跨层公开类型的兼容性。</summary>
public sealed class WorksDirectoryStructureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> _expectedFolders = new Dictionary<string, string[]>
    {
        ["Core"] =
        [
            "CreateWork", "DeleteWork", "DeleteWorkResult", "GetWork", "ListWorks", "SelectWorkImage",
            "IWorkDeletionRepository",
            "IWorkImageSelectionRepository", "IWorkRepository", "WorkSnapshot"
        ],
        ["ImageGeneration"] =
        [
            "ImageGenerationService", "ImageGenerationUnavailableException",
            "IGeneratedImageStore", "IQueuedImageGenerationReader", "QueuedImageGeneration"
        ],
        ["ModelGeneration"] =
        [
            "DispatchQueuedModelGeneration", "IGeneratedModelStore", "IModelGenerationClaimRepository",
            "IModelGenerationResultRepository", "IModelUploadStore", "IQueuedModelGenerationReader",
            "QueuedModelGeneration", "SaveModelGenerationResult"
        ],
        ["Media"] = ["IOriginalImageReader", "IOriginalImageStore", "ImageFileLimits"],
        ["Administration"] = ["AiGenerationTask", "IAiGenerationTaskReader", "IAdminWorksReader", "ListAdminWorks"],
        ["Recovery"] =
        [
            "IAdminImageReviewRepository", "IAdminModelReviewRepository",
            "IStalledImageTasksReader", "IStalledModelTasksReader",
            "ListStalledImageTasks", "ListStalledModelTasks",
            "ReviewStalledImageTask", "ReviewStalledModelTask",
            "StalledImageTask", "StalledModelTask"
        ]
    };

    /// <summary>每个文件只出现在职责所属目录，根目录不得再堆积 C# 代码或遗漏未分类的类。</summary>
    [Fact]
    public void Works_FilesAreExclusivelyGroupedByBusinessResponsibility()
    {
        var root = FindWorksDirectory();
        var expected = _expectedFolders
            .SelectMany(group => group.Value.Select(name => Path.Combine(group.Key, name + ".cs")))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var actual = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(40, expected.Length);
        Assert.Equal(expected, actual);
    }

    /// <summary>本次仅整理物理目录，保留已有应用层端口及用例的公共命名空间供调用方继续使用。</summary>
    [Fact]
    public void Works_PublicTypeNamespacesRemainBackwardCompatible()
    {
        Assert.Equal("Tongyin.Application.Works", typeof(CreateWork).Namespace);
        Assert.Equal("Tongyin.Application.Works", typeof(ImageGenerationService).Namespace);
        Assert.Equal("Tongyin.Application.Works", typeof(SaveModelGenerationResult).Namespace);
        Assert.Equal("Tongyin.Application.Works", typeof(ReviewStalledImageTask).Namespace);
        Assert.Equal("Tongyin.Application.Works", typeof(IAdminWorksReader).Namespace);
    }

    private static string FindWorksDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var repoRoot = Path.Combine(directory.FullName, "backend-dotnet", "src", "Tongyin.Application", "Works");
            if (Directory.Exists(repoRoot))
            {
                return repoRoot;
            }

            var solutionRoot = Path.Combine(directory.FullName, "src", "Tongyin.Application", "Works");
            if (Directory.Exists(solutionRoot))
            {
                return solutionRoot;
            }
        }

        throw new DirectoryNotFoundException("找不到童印 Application.Works 源代码目录");
    }
}
