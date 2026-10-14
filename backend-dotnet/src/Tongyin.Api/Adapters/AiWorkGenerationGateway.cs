using Tongyin.Api.Core;
using Tongyin.Application.Works;
using Tongyin.Domain.Works;
using Tongyin.Infrastructure.Persistence;

namespace Tongyin.Api.Adapters;

// Temporary outbound adapter for the existing AI engine.
// It can move to Infrastructure once the old AI engine is split into a pure port.
public sealed class AiWorkGenerationGateway(AiService ai) : IWorkGenerationGateway
{
    public void Dispatch(WorkState work,string stage) =>
        ai.Dispatch(LegacyWorkDocument.ToDocument(work),stage);

    public Task<bool> IsEnabledAsync(string stage)=>ai.Enabled(stage);
}
