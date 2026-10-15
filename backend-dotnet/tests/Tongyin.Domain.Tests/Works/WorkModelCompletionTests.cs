using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证已确认图片的作品接收三维模型生成结果的业务规则。
/// </summary>
public sealed class WorkModelCompletionTests
{
    /// <summary>
    /// 当前模型任务成功后保存 GLB 路径，并进入模型就绪状态。
    /// </summary>
    [Fact]
    public void CompleteModelGeneration_CurrentTask_StoresGlbAndMarksReady()
    {
        var work = CreateModelPendingWork();

        work.CompleteModelGeneration("generation-001", "media/model.glb");

        Assert.Equal(WorkStatus.ModelReady, work.Status);
        Assert.Equal("media/model.glb", work.Model);
        Assert.Equal(0, work.Selected);
        Assert.Equal("media/image.png", Assert.Single(work.Candidates));
    }

    /// <summary>
    /// 过期生成任务不能覆盖当前作品的模型或处理状态。
    /// </summary>
    [Fact]
    public void CompleteModelGeneration_StaleTask_RejectsWithoutMutation()
    {
        var work = CreateModelPendingWork();

        Assert.Throws<InvalidOperationException>(() => work.CompleteModelGeneration("generation-old", "media/old.glb"));

        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal(string.Empty, work.Model);
    }

    /// <summary>
    /// 用户尚未确认图片时，不能提前接收模型结果。
    /// </summary>
    [Fact]
    public void CompleteModelGeneration_BeforeImageSelection_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "media/image.png");

        Assert.Throws<InvalidOperationException>(() => work.CompleteModelGeneration("generation-001", "media/model.glb"));

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal(string.Empty, work.Model);
    }

    /// <summary>
    /// 模型已经就绪后，不允许重复回调替换已保存的模型。
    /// </summary>
    [Fact]
    public void CompleteModelGeneration_AfterModelReady_RejectsDuplicate()
    {
        var work = CreateModelPendingWork();
        work.CompleteModelGeneration("generation-001", "media/first.glb");

        Assert.Throws<InvalidOperationException>(() => work.CompleteModelGeneration("generation-001", "media/second.glb"));

        Assert.Equal(WorkStatus.ModelReady, work.Status);
        Assert.Equal("media/first.glb", work.Model);
    }

    /// <summary>
    /// 未提供 GLB 文件路径不能把作品标记为模型生成完成。
    /// </summary>
    [Fact]
    public void CompleteModelGeneration_EmptyModelPath_Rejects()
    {
        var work = CreateModelPendingWork();

        Assert.Throws<ArgumentException>(() => work.CompleteModelGeneration("generation-001", string.Empty));

        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal(string.Empty, work.Model);
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
