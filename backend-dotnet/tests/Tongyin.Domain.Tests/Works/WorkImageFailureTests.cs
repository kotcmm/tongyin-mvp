using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证形象图片生成失败后，错误归属及可重试的业务规则。
/// </summary>
public sealed class WorkImageFailureTests
{
    /// <summary>
    /// 当前任务失败时，作品应保存失败原因并转入失败状态。
    /// </summary>
    [Fact]
    public void FailImageGeneration_CurrentTask_RecordsFailure()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");

        work.FailImageGeneration("generation-001", "上游生成失败");

        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal("上游生成失败", work.Note);
        Assert.Equal(1, work.ImageAttempts);
    }

    /// <summary>
    /// 旧任务的失败通知不能覆盖当前正在进行的生成任务。
    /// </summary>
    [Fact]
    public void FailImageGeneration_StaleTask_RejectsWithoutChangingWork()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-new");

        Assert.Throws<InvalidOperationException>(() => work.FailImageGeneration("generation-old", "旧任务失败"));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal(string.Empty, work.Note);
        Assert.Equal("generation-new", work.GenerationId);
    }

    /// <summary>
    /// 已经成功的作品不得被迟到的失败回调覆盖。
    /// </summary>
    [Fact]
    public void FailImageGeneration_AfterImageReady_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "result.png");

        Assert.Throws<InvalidOperationException>(() => work.FailImageGeneration("generation-001", "迟到的失败消息"));

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal("result.png", Assert.Single(work.Candidates));
    }

    /// <summary>
    /// 已失败的任务不得重复接收失败回调。
    /// </summary>
    [Fact]
    public void FailImageGeneration_AfterFailure_RejectsDuplicate()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.FailImageGeneration("generation-001", "第一次错误");

        Assert.Throws<InvalidOperationException>(() => work.FailImageGeneration("generation-001", "第二次错误"));

        Assert.Equal("第一次错误", work.Note);
    }

    /// <summary>
    /// 未发起生成任务的作品不能被直接标记为生成失败。
    /// </summary>
    [Fact]
    public void FailImageGeneration_WithoutActiveTask_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");

        Assert.Throws<InvalidOperationException>(() => work.FailImageGeneration("", "未发起的任务"));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal(0, work.ImageAttempts);
    }

    /// <summary>
    /// 在可用生成次数内，失败作品可以重新生成并清除旧错误。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_AfterFailure_ResetsFailureAndStartsNewTask()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.FailImageGeneration("generation-001", "上游异常");

        work.RequestImageGeneration("generation-002", 2);

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal("generation-002", work.GenerationId);
        Assert.Equal(2, work.ImageAttempts);
        Assert.Equal(string.Empty, work.Note);
        Assert.Empty(work.Candidates);
    }

    /// <summary>
    /// 生成额度耗尽后不得重新提交，也不能修改已保存的失败记录。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_QuotaExhausted_RejectsWithoutMutation()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.FailImageGeneration("generation-001", "上游异常");

        Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-002", 1));

        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal("generation-001", work.GenerationId);
        Assert.Equal("上游异常", work.Note);
        Assert.Equal(1, work.ImageAttempts);
    }
}
