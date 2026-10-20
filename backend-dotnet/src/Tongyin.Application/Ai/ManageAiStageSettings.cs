namespace Tongyin.Application.Ai;

/// <summary>审核图片/模型服务商的管理员配置，防止无效或不安全的地址进入自动派发。</summary>
public sealed class ManageAiStageSettings(IAdminAiSettingsStore store, IModelGenerationClientResolver modelClients)
{
    /// <summary>获取图片或模型的运行配置与加密密钥设置状态。</summary>
    public Task<AiStageConfiguration> GetAsync(string stage, CancellationToken cancellationToken = default)
    {
        ValidateStage(stage);
        return store.GetAsync(stage, cancellationToken);
    }

    /// <summary>校验 HTTPS 与已注册 Adapter 协议，然后原子写入。</summary>
    public Task<AiStageConfiguration> SaveAsync(string stage, UpdateAiStageConfiguration settings,
        CancellationToken cancellationToken = default)
    {
        ValidateStage(stage);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Endpoint.Length > 1000 || settings.Model.Length > 128
            || (settings.ApiKey?.Length ?? 0) > 1024 || settings.PromptTemplate.Length > 4000)
        {
            throw new ArgumentException("生成配置的文本长度超出允许范围");
        }

        if (string.IsNullOrWhiteSpace(settings.PromptTemplate))
        {
            throw new ArgumentException("提示词不能为空");
        }

        if (settings.Enabled && (string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.Model)))
        {
            throw new ArgumentException("启用生成服务前必须填写 API 地址和模型标识");
        }

        var adapter = string.IsNullOrWhiteSpace(settings.Adapter)
            ? (stage == "image" ? "seedream" : "hunyuan3d")
            : settings.Adapter.Trim();
        if (stage == "image" && adapter != "seedream")
            throw new ArgumentException("尚未注册该图片生成适配器");

        // 图片接口暂只注册官方 Seedream；3D 地址规则则交由选中的协议 Adapter 校验。
        if (stage == "image" && !string.IsNullOrWhiteSpace(settings.Endpoint)
            && settings.Endpoint != "https://ark.cn-beijing.volces.com/api/v3/images/generations")
            throw new ArgumentException("图片生成当前仅支持火山方舟 Seedream 官方接口");

        if (stage == "model")
        {
            var client = modelClients.Resolve(adapter);
            if (!string.IsNullOrWhiteSpace(settings.Endpoint))
                client.ValidateEndpoint(settings.Endpoint, settings.Model);
        }

        return store.SaveAsync(stage, new UpdateAiStageConfiguration
        {
            Adapter = adapter, Endpoint = settings.Endpoint, Model = settings.Model,
            Enabled = settings.Enabled, ApiKey = settings.ApiKey,
            PromptTemplate = settings.PromptTemplate
        }, cancellationToken);
    }

    private static void ValidateStage(string stage)
    {
        if (stage is not ("image" or "model"))
        {
            throw new ArgumentException("仅支持 image 和 model 两种生成阶段");
        }
    }
}
