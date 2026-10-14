using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Application;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
[ApiController]
public sealed class WorksController(WorkService service) : ControllerBase
{
    [HttpPost("/api/works")]
    public Task<IResult> UploadDrawing() => service.UploadDrawing(HttpContext);

    [HttpGet("/api/works")]
    public Task<IResult> ListWorks() => service.ListWorks(HttpContext);

    [HttpGet("/api/works/{id}")]
    public Task<IResult> GetWork([FromRoute] string id) => service.GetWork(id, HttpContext);

    [HttpPost("/api/works/{id}/generate")]
    public Task<IResult> GenerateImage([FromRoute] string id) => service.GenerateImage(id, HttpContext);

    [HttpPost("/api/works/{id}/select")]
    public Task<IResult> SelectImage([FromRoute] string id) => service.SelectImage(id, HttpContext);
}
