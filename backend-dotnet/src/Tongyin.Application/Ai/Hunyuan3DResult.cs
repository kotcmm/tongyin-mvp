namespace Tongyin.Application.Ai;

/// <summary>由混元真实查询接口返回的任务状态，不能推断百分比。</summary>
public sealed record Hunyuan3DResult(string Status, string? GlbUrl, string? Error);
