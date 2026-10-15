namespace Tongyin.Application.Works;

/// <summary>
/// 原画上传的统一业务大小限制，供 API 和文件存储适配器共同使用。
/// </summary>
public static class OriginalImageLimits
{
    /// <summary>
    /// 单张原画最大容量：10 MiB。
    /// </summary>
    public const long MaxBytes = 10L << 20;
}
