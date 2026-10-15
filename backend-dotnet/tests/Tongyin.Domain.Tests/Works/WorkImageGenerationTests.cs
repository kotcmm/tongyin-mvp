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
    /// <summary>
    /// 图片生成完成但尚未选图时，可以再次生成，原候选图会失效。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_ImageReady_AllowsRetryAndClearsCandidates()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "cartoon.png");

        work.RequestImageGeneration("generation-002");

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal("generation-002", work.GenerationId);
        Assert.Equal(2, work.ImageAttempts);
        Assert.Empty(work.Candidates);
    }

    /// <summary>
    /// 用户确认图片、进入模型阶段后，不允许重新发起图片生成。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_ModelPending_RejectsWithoutChangingSelection()
    {
        var work = CreateModelPendingWork();

        var error = Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-002"));

        Assert.Equal("当前作品不能重新生成形象", error.Message);
        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal(0, work.Selected);
        Assert.Equal("cartoon.png", Assert.Single(work.Candidates));
        Assert.Equal("generation-001", work.GenerationId);
        Assert.Equal(1, work.ImageAttempts);
    }

    /// <summary>
    /// 已经生成 3D 模型的作品不能被图片重试意外覆盖。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_ModelReady_RejectsWithoutChangingModel()
    {
        var work = CreateModelPendingWork();
        work.CompleteModelGeneration("generation-001", "figure.glb");

        var error = Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-002"));

        Assert.Equal("当前作品不能重新生成形象", error.Message);
        Assert.Equal(WorkStatus.ModelReady, work.Status);
        Assert.Equal("figure.glb", work.Model);
        Assert.Equal(0, work.Selected);
        Assert.Equal(1, work.ImageAttempts);
    }

    /// <summary>
    /// 失败发生在模型阶段时，不能误当成图片生成失败再重试。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_ModelFailed_RejectsWithoutChangingWork()
    {
        var work = CreateModelPendingWork();
        work.FailModelGeneration("generation-001", "模型生成失败");

        var error = Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-002"));

        Assert.Equal("当前作品不能重新生成形象", error.Message);
        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal(0, work.Selected);
        Assert.Equal("模型生成失败", work.Note);
        Assert.Equal(1, work.ImageAttempts);
    }

    /// <summary>
    /// 尚未配置有效次数时，拒绝登记任务且不改变作品状态。
    /// </summary>
    [Fact]
    public void RequestImageGeneration_ZeroQuota_RejectsWithoutMutation()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");

        Assert.Throws<InvalidOperationException>(() => work.RequestImageGeneration("generation-001", 0));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal(0, work.ImageAttempts);
        Assert.Equal(string.Empty, work.GenerationId);
    }

    private static Work CreateModelPendingWork()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "drawing.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "cartoon.png");
        work.SelectImage(0);
        return work;
    }

}
