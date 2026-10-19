namespace Tongyin.Application.Works;

/// <summary>图片服务不可用时阻止登记任务，由 API 将此业务状态转换为 HTTP 503。</summary>
public sealed class ImageGenerationUnavailableException : Exception
{
    /// <summary>不回显服务商密钥或内部配置细节。</summary>
    public ImageGenerationUnavailableException() : base("图片生成服务暂不可用") { }
}
