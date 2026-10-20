using Tongyin.Application.Ai;
using Xunit;

namespace Tongyin.Application.Tests.Works;

public sealed class AiGenerationSettingsTests
{
    [Fact]
    public void CanGenerateModel_DoesNotRestrictApiUrlOrModelVersion()
    {
        var settings = new AiGenerationSettings
        {
            Stage = "model", PromptTemplate = "3D", PromptVersion = 1,
            Provider = new AiProviderSettings
            {
                Stage = "model", Adapter = "hunyuan3d",
                Endpoint = "https://gateway.example/v1/ai3d/submit",
                Model = "future-model", Enabled = true, Default = true
            }
        };
        Assert.True(settings.CanGenerateModel());
    }

    [Fact]
    public void CanGenerateModel_RequiresEnabledDefaultModelStage()
    {
        var settings = new AiGenerationSettings
        {
            Stage = "image", PromptTemplate = "3D", PromptVersion = 1,
            Provider = new AiProviderSettings
            {
                Stage = "model", Adapter = "hunyuan3d",
                Endpoint = "https://gateway.example/v1/ai3d/submit",
                Model = "future-model", Enabled = true, Default = true
            }
        };
        Assert.False(settings.CanGenerateModel());
    }
}
