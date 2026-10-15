using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证用户首次请求生成儿童画形象时的领域行为。
/// </summary>
public sealed class WorkImageGenerationTests
{
    /// <summary>
    /// 首次提交生成任务会消耗一次机会并保存当前生成任务标识。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_FirstRequest_RecordsGeneration()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        work.RequestImageGeneration("generation-001");

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal(1, work.ImageAttempts);
        Assert.Equal("generation-001", work.GenerationId);
    }

    /// <summary>
    /// 正在生成的任务不能再次提交，避免重复消耗生成次数。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_WhilePending_RejectsDuplicate()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");
        work.RequestImageGeneration("generation-001");

        Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-002"));

        Assert.Equal(1, work.ImageAttempts);
        Assert.Equal("generation-001", work.GenerationId);
    }
}
