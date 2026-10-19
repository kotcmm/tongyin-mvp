namespace Tongyin.Application.Ai;

/// <summary>混元生 3D 提交及查询端口；外部请求结果不确定时不得重新提交收费任务。</summary>
public interface IHunyuan3DClient
{
    /// <summary>提交一张本地卡通图；成功返回混元官方 JobId。</summary>
    Task<string> SubmitAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> image, CancellationToken ct = default);

    /// <summary>按腾讯云 JobId 查询当前状态和已生成 GLB 下载地址。</summary>
    Task<Hunyuan3DResult> QueryAsync(AiGenerationSettings settings, string jobId, CancellationToken ct = default);
}
