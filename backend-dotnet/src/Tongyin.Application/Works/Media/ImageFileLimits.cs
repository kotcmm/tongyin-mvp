namespace Tongyin.Application.Works;

/// <summary>
/// 统一约束原画、AI 生成图片和管理员补录图片的文件容量。
/// </summary>
public static class ImageFileLimits
{
    /// <summary>
    /// 单张图片的最大容量：10 MiB。
    /// </summary>
    public const long MaxBytes = 10L * 1024 * 1024;
}
