using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Tongyin.Infrastructure.Persistence;

namespace Tongyin.Api.Adapters;

// Temporary outbound adapter for the existing AI engine.
// It can move to Infrastructure once the old AI engine is split into a pure port.
/// <summary>
/// AI（人工智能）生成网关适配器，转接现有生图及模型任务。
/// </summary>
public sealed class AiWorkGenerationGateway(AiService ai) : IWorkGenerationGateway
{
    /// <summary>
    /// 派发形象或模型生成任务。
    /// </summary>
    public void Dispatch(WorkState work,string stage) =>
        ai.Dispatch(LegacyWorkDocument.ToDocument(work),stage);

    /// <summary>
    /// 查询指定生成阶段是否启用自动任务。
    /// </summary>
    public Task<bool> IsEnabledAsync(string stage)=>ai.Enabled(stage);
}
