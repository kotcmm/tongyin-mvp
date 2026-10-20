namespace Tongyin.Application.Ai;

/// <summary>通过配置的 Adapter 标识解析已注册的 3D 协议实现。</summary>
public interface IModelGenerationClientResolver
{
    IModelGenerationClient Resolve(string adapter);
}
