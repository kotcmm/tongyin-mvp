namespace Tongyin.Application.Ai;

/// <summary>审核图片/模型服务商的管理员配置，防止无效或不安全的地址进入自动派发。</summary>
public sealed class ManageAiStageSettings(IAdminAiSettingsStore store)
{
    /// <summary>获取图片或模型的运行配置与加密密钥设置状态。</summary>
    public Task<AiStageConfiguration> GetAsync(string stage, CancellationToken cancellationToken = default)
    {
        ValidateStage(stage);
        return store.GetAsync(stage, cancellationToken);
    }

    /// <summary>拒绝不匹配当前固定适配器的配置，然后原子写入。</summary>
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

        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            if (stage == "image")
            {
                if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)
                    || uri.AbsoluteUri != "https://ark.cn-beijing.volces.com/api/v3/images/generations")
                {
                    throw new ArgumentException("图片生成目前仅支持火山方舟 Seedream 官方接口");
                }
            }
            else if (settings.Endpoint != "https://api.ai3d.cloud.tencent.com/v1/ai3d/submit"
                || settings.Model is not ("3.0" or "3.1"))
            {
                throw new ArgumentException("当前仅支持腾讯混元 3D 专业版，模型填写 3.0 或 3.1");
            }
        }

        return store.SaveAsync(stage, settings, cancellationToken);
    }

    private static void ValidateStage(string stage)
    {
        if (stage is not ("image" or "model"))
        {
            throw new ArgumentException("仅支持 image 和 model 两种生成阶段");
        }
    }
}
