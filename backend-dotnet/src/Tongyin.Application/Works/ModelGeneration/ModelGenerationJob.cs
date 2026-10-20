namespace Tongyin.Application.Works;

/// <summary>已被混元接受的 3D 生成任务，JobId 必须在查询前持久化。</summary>
public sealed record ModelGenerationJob(string WorkId, string GenerationId, string ProviderJobId, string ProviderId, string ApiKeyCipher, string Model, string Adapter, string Endpoint);
