namespace Tongyin.Application.Ai;

/// <summary>
/// 生成单张卡通图片的 AI 模型调用端口，基础设施按实际模型协议实现。
/// </summary>
public interface IImageGenerationClient
{
    /// <summary>
    /// 调用模型生成图片并返回 HTTPS 结果地址，不负责图片下载或持久化。
    /// </summary>
    Task<string> GenerateAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> originalImage, CancellationToken cancellationToken = default);
}
