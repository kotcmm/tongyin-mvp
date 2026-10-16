using Microsoft.AspNetCore.Mvc;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>
/// 管理员补录已生成图片的表单，使用 multipart 上传本地图片而不调用第三方模型。
/// </summary>
public sealed class AdminImageCompletionReviewRequest
{
    /// <summary>需处理的生成任务编号。</summary>
    [FromForm(Name = "generation_id")]
    public string? GenerationId { get; init; }

    /// <summary>人工确认图片生成成功的核实依据。</summary>
    [FromForm(Name = "reason")]
    public string? Reason { get; init; }

    /// <summary>管理员取得的真实图片文件，最大 10 MiB。</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}
