using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 微信客户身份认证与用户资料接口。
/// </summary>
[ApiController]
public sealed class CustomerController(CustomerService service) : ControllerBase
{
    /// <summary>
    /// 通过微信临时登录凭据认证客户。
    /// </summary>
    [HttpPost("/api/auth/wechat")]
    public Task<IResult> LoginWechat() => service.LoginWechat(HttpContext);

    /// <summary>
    /// 获取当前登录用户的资料。
    /// </summary>
    [HttpGet("/api/me")]
    public Task<IResult> GetProfile() => service.GetProfile(HttpContext);

    /// <summary>
    /// 验证资源签名并返回媒体文件。
    /// </summary>
    [HttpGet("/media/{file}")]
    public Task<IResult> ServeMedia([FromRoute] string file) => service.ServeMedia(file, HttpContext);
}
