using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证三维模型生成失败回调只作用于当前待生成任务。
/// </summary>
public sealed class WorkModelFailureTests
{
    /// <summary>
    /// 当前模型任务失败时记录原因并保留用户已经确认的形象图片。
    /// </summary>
    [Fact]
    public void FailModelGeneration_CurrentTask_RecordsReasonAndPreservesImage()
    {
        var work = CreateModelPendingWork();

        work.FailModelGeneration("generation-001", "三维模型服务不可用");

        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal("三维模型服务不可用", work.Note);
        Assert.Equal(0, work.Selected);
        Assert.Equal("media/image.png", Assert.Single(work.Candidates));
        Assert.Equal(string.Empty, work.Model);
    }

    /// <summary>
    /// 过期任务失败通知不得修改当前正在生成模型的作品。
    /// </summary>
    [Fact]
    public void FailModelGeneration_StaleTask_RejectsWithoutMutation()
    {
        var work = CreateModelPendingWork();

        Assert.Throws<InvalidOperationException>(() => work.FailModelGeneration("generation-old", "旧任务错误"));

        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal(string.Empty, work.Note);
        Assert.Equal("generation-001", work.GenerationId);
    }

    /// <summary>
    /// 模型已就绪后收到的失败通知不得覆盖成功结果。
    /// </summary>
    [Fact]
    public void FailModelGeneration_AfterModelReady_RejectsWithoutMutation()
    {
        var work = CreateModelPendingWork();
        work.CompleteModelGeneration("generation-001", "media/model.glb");

        Assert.Throws<InvalidOperationException>(() => work.FailModelGeneration("generation-001", "迟到的失败通知"));

        Assert.Equal(WorkStatus.ModelReady, work.Status);
        Assert.Equal("media/model.glb", work.Model);
        Assert.Equal(string.Empty, work.Note);
    }

    /// <summary>
    /// 同一模型生成任务已经失败后，不能再次写入失败原因。
    /// </summary>
    [Fact]
    public void FailModelGeneration_AlreadyFailed_RejectsDuplicate()
    {
        var work = CreateModelPendingWork();
        work.FailModelGeneration("generation-001", "第一次失败");

        Assert.Throws<InvalidOperationException>(() => work.FailModelGeneration("generation-001", "第二次失败"));

        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal("第一次失败", work.Note);
    }

    /// <summary>
    /// 图片尚未被用户确认时，不允许把作品标记为模型生成失败。
    /// </summary>
    [Fact]
    public void FailModelGeneration_BeforeImageSelection_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "media/image.png");

        Assert.Throws<InvalidOperationException>(() => work.FailModelGeneration("generation-001", "模型失败"));

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal(string.Empty, work.Note);
    }

    private static Work CreateModelPendingWork()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "media/image.png");
        work.SelectImage(0);
        return work;
    }
}
