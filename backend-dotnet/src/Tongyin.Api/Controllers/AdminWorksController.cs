using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Auth;
using Tongyin.Api.ViewModels.Works;
using Tongyin.Application.Works;
using Tongyin.Infrastructure.Media;

namespace Tongyin.Api.Controllers;

/// <summary>运营人员跨用户检查原画、AI 形象和 GLB 模型的只读接口。</summary>
[ApiController]
[Route("api/admin/works")]
[AdminAuthorize]
public sealed class AdminWorksController(ListAdminWorks listWorks, LocalMediaStore media) : ControllerBase
{
    /// <summary>只允许管理员读取全部作品；媒体地址重新签名，不公开微信 OpenID。</summary>
    [HttpGet]
    public async Task<ActionResult<WorkResponse[]>> ListAsync(CancellationToken cancellationToken)
    {
        var works = await listWorks.ExecuteAsync(cancellationToken);
        return Ok(works.Select(work => WorkResponse.FromSnapshot(work, media.SignUrl)).ToArray());
    }
}
