using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Application;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class CustomerController(CustomerService service) : ControllerBase
{
    [HttpPost("/api/auth/wechat")]
    public Task<IResult> LoginWechat() => service.LoginWechat(HttpContext);

    [HttpGet("/api/me")]
    public Task<IResult> GetProfile() => service.GetProfile(HttpContext);

    [HttpGet("/media/{file}")]
    public IResult ServeMedia([FromRoute] string file) => service.ServeMedia(file, HttpContext);
}
