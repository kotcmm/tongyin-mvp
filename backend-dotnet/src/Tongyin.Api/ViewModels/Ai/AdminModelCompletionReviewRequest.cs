using Microsoft.AspNetCore.Mvc;

namespace Tongyin.Api.ViewModels.Ai;

/// <summary>管理员上传经过人工核实的真实 GLB 模型的 multipart 表单 DTO（数据传输对象）。</summary>
public sealed class AdminModelCompletionReviewRequest
{
    /// <summary>当前模型生成任务编号。</summary>
    [FromForm(Name = "generation_id")]
    public string? GenerationId { get; init; }

    /// <summary>核实来源的审计说明。</summary>
    [FromForm(Name = "reason")]
    public string? Reason { get; init; }

    /// <summary>真实 GLB 二进制模型文件，最大 100 MiB。</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}
