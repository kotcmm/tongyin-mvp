using Tongyin.Application.Ai;

namespace Tongyin.Infrastructure.Ai;

/// <summary>查找已注册的模型协议适配器；未知适配器必须明确拒绝，不能误发收费请求。</summary>
public sealed class ModelGenerationClientResolver(IEnumerable<IModelGenerationClient> clients)
    : IModelGenerationClientResolver
{
    public IModelGenerationClient Resolve(string adapter)
        => clients.SingleOrDefault(client => client.Adapter == adapter)
           ?? throw new ArgumentException("未注册的 3D 生成适配器", nameof(adapter));
}
