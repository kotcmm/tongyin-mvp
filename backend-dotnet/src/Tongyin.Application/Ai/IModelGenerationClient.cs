namespace Tongyin.Application.Ai;

/// <summary>单一 3D 服务商的调用协议；新的服务商通过注册 Adapter 接入。</summary>
public interface IModelGenerationClient
{
    string Adapter { get; }
    /// <summary>保存配置之前检查该协议的提交地址和模型参数。</summary>
    void ValidateEndpoint(string endpoint, string model);
    /// <summary>认领收费任务之前额外确认凭据可解密。</summary>
    void ValidateConfiguration(AiProviderSettings provider);
    Task<string> SubmitAsync(AiGenerationSettings settings, ReadOnlyMemory<byte> image, CancellationToken ct = default);
    Task<Hunyuan3DResult> QueryAsync(AiGenerationSettings settings, string jobId, CancellationToken ct = default);
}
