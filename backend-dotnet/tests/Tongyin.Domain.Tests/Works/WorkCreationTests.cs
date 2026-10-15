using Tongyin.Domain.Works;
using Xunit;

namespace Tongyin.Domain.Tests.Works;

/// <summary>
/// 验证上传原画后创建作品的初始业务状态。
/// </summary>
public sealed class WorkCreationTests
{
    /// <summary>
    /// 创建作品时应保存业务身份、所属用户、原画和创建时间。
    /// </summary>
    [Fact]
    public void Create_RecordsOriginalDrawingAndOwnership()
    {
        var createdAt = new DateTimeOffset(2026, 10, 15, 10, 30, 0, TimeSpan.FromHours(8));

        var work = Work.Create("work-001", "openid-001", createdAt, "media/drawing.png");

        Assert.Equal("work-001", work.Id);
        Assert.Equal("openid-001", work.Owner);
        Assert.Equal(createdAt, work.CreatedAt);
        Assert.Equal("media/drawing.png", work.Original);
    }

    /// <summary>
    /// 新作品等待生成图片，不应提前进入图片就绪状态。
    /// </summary>
    [Fact]
    public void Create_StartsInImagePendingStatus()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Equal("image_pending", work.Status);
    }

    /// <summary>
    /// 新作品还没有被选中的图片。
    /// </summary>
    [Fact]
    public void Create_StartsWithoutSelectedImage()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Equal(-1, work.Selected);
    }

    /// <summary>
    /// 新作品不应存在已生成的候选图或预览图。
    /// </summary>
    [Fact]
    public void Create_StartsWithoutGeneratedAssets()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Empty(work.Candidates);
        Assert.Empty(work.Preview);
    }

    /// <summary>
    /// 上传原画不消耗 AI 生图次数，次数在请求生成时才计算。
    /// </summary>
    [Fact]
    public void Create_DoesNotConsumeImageAttempts()
    {
        var work = Work.Create("work-001", "openid-001", DateTimeOffset.UtcNow, "media/drawing.png");

        Assert.Equal(0, work.ImageAttempts);
    }
}
