using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证单张形象图片生成成功后的领域状态转换。
/// </summary>
public sealed class WorkImageCompletionTests
{
    /// <summary>
    /// 当前任务生成成功后，记录单张图片并进入图片就绪状态。
    /// </summary>
    [Fact]
    public void CompleteImageGeneration_CurrentTask_StoresSingleImageAndMarksReady()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");
        work.RequestImageGeneration("generation-001");

        work.CompleteImageGeneration("generation-001", "media/generated.png");

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal("media/generated.png", Assert.Single(work.Candidates));
        Assert.Equal(1, work.ImageAttempts);
    }

    /// <summary>
    /// 不属于当前生成任务的结果不得覆盖作品状态或图片。
    /// </summary>
    [Fact]
    public void CompleteImageGeneration_OldTask_RejectsWithoutChangingWork()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");
        work.RequestImageGeneration("generation-new");

        Assert.Throws<InvalidOperationException>(() => work.CompleteImageGeneration("generation-old", "media/old.png"));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Empty(work.Candidates);
        Assert.Equal("generation-new", work.GenerationId);
    }

    /// <summary>
    /// 已完成的任务不能重复应用图片结果。
    /// </summary>
    [Fact]
    public void CompleteImageGeneration_AlreadyReady_RejectsDuplicate()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "media/first.png");

        Assert.Throws<InvalidOperationException>(() => work.CompleteImageGeneration("generation-001", "media/duplicate.png"));

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal("media/first.png", Assert.Single(work.Candidates));
    }

    /// <summary>
    /// 尚未发起生成任务的作品不能接受伪造的生成结果。
    /// </summary>
    [Fact]
    public void CompleteImageGeneration_NoActiveTask_RejectsUnrequestedResult()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Throws<InvalidOperationException>(() => work.CompleteImageGeneration("", "media/unsolicited.png"));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Empty(work.Candidates);
    }
}
