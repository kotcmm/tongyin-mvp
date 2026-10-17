using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tongyin.Application.Ai;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Infrastructure.Ai;

/// <summary>调用原 Go 约定的异步 3D worker 协议，不暴露密钥或模型任务详情到日志。</summary>
public sealed class ModelWorkerClient(HttpClient httpClient, GoAiKeyDecryptor decryptor, LocalMediaStore media, IConfiguration configuration) : IModelWorkerClient
{
    /// <summary>提交模型任务，发送确认图片签名地址和安全回调 URL；结果由独立回调保存。</summary>
    public async Task SubmitAsync(AiGenerationSettings settings, WorkSnapshot work, CancellationToken cancellationToken = default)
    {
        if (!settings.CanGenerateModel())
        {
            throw new InvalidOperationException("未配置可用的 3D worker");
        }
        if (work.Status != "model_pending" || work.Selected != 0 || work.Candidates.Count == 0
            || string.IsNullOrWhiteSpace(work.GenerationId))
        {
            throw new InvalidOperationException("模型任务状态无效");
        }

        var provider = settings.Provider!;
        var endpoint = RequirePublicHttpsUri(provider.Endpoint);
        var publicUrl = configuration["PUBLIC_URL"];
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var callbackBase)
            || callbackBase.Scheme != Uri.UriSchemeHttps || callbackBase.UserInfo.Length != 0)
        {
            throw new InvalidOperationException("模型 worker 需要配置公网 HTTPS PUBLIC_URL");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                work_id = work.Id,
                stage = "model",
                generation_id = work.GenerationId,
                source_url = media.SignUrl(work.Candidates[0]),
                callback_url = publicUrl!.TrimEnd('/') + "/api/internal/ai-result",
                instructions = settings.PromptTemplate,
                prompt_version = settings.PromptVersion,
                model = provider.Model
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", decryptor.Decrypt(provider.ApiKeyCipher));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("3D worker 未确认接收任务，已认领但不会自动重试");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("3D worker 连接超时，提交状态未知；请人工核对");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("3D worker 连接异常，提交状态未知；请人工核对");
        }
    }

    private static Uri RequirePublicHttpsUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || uri.Port != 443
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("3D worker 只接受安全的公网 HTTPS 配置地址");
        }
        return uri;
    }
}
