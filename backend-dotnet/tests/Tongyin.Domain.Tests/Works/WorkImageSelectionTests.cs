using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证用户确认生成图片后进入三维模型生成阶段的业务规则。
/// </summary>
public sealed class WorkImageSelectionTests
{
    /// <summary>
    /// 图片已就绪时确认唯一候选图，作品进入三维模型待生成状态。
    /// </summary>
    [Fact]
    public void SelectImage_ReadyImage_StartsModelGeneration()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "candidate.png");

        work.SelectImage(0);

        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal(0, work.Selected);
    }

    /// <summary>
    /// 尚未生成图片的作品不能确认形象。
    /// </summary>
    [Fact]
    public void SelectImage_BeforeImageReady_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "original.png");

        Assert.Throws<InvalidOperationException>(() => work.SelectImage(0));

        Assert.Equal(WorkStatus.ImagePending, work.Status);
        Assert.Equal(-1, work.Selected);
    }

    /// <summary>
    /// 单图版本只能选择第零张图片，非法索引不得修改作品。
    /// </summary>
    [Fact]
    public void SelectImage_NonZeroIndex_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "candidate.png");

        Assert.Throws<InvalidOperationException>(() => work.SelectImage(1));

        Assert.Equal(WorkStatus.ImageReady, work.Status);
        Assert.Equal(-1, work.Selected);
    }

    /// <summary>
    /// 图片生成失败时不能跳过失败状态直接请求三维模型。
    /// </summary>
    [Fact]
    public void SelectImage_AfterImageGenerationFailed_Rejects()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "original.png");
        work.RequestImageGeneration("generation-001");
        work.FailImageGeneration("generation-001", "图片生成失败");

        Assert.Throws<InvalidOperationException>(() => work.SelectImage(0));

        Assert.Equal(WorkStatus.Failed, work.Status);
        Assert.Equal(-1, work.Selected);
    }

    /// <summary>
    /// 已确认图片的作品不能重复提交三维模型生成请求。
    /// </summary>
    [Fact]
    public void SelectImage_AlreadyModelPending_RejectsDuplicate()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "original.png");
        work.RequestImageGeneration("generation-001");
        work.CompleteImageGeneration("generation-001", "candidate.png");
        work.SelectImage(0);

        Assert.Throws<InvalidOperationException>(() => work.SelectImage(0));

        Assert.Equal(WorkStatus.ModelPending, work.Status);
        Assert.Equal("candidate.png", Assert.Single(work.Candidates));
    }
}
