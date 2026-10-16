using Tongyin.Application.Works;

namespace Tongyin.Api.Http;

/// <summary>
/// 上传图片时允许的 HTTP 请求体大小，包含图片和表单编码开销。
/// </summary>
public static class UploadRequestLimits
{
    /// <summary>
    /// 上传请求最大容量：图片最大容量再预留 1 MiB 表单开销。
    /// </summary>
    public const long MaxRequestBytes = ImageFileLimits.MaxBytes + (1L << 20);
}
