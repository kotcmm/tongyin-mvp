using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Presentation;

namespace Tongyin.Api.Controllers;

// HTTP routing and parameter binding only. Business rules live in Application.
/// <summary>
/// 儿童画作品上传、查询、形象生成与选择接口。
/// </summary>
[ApiController]
public sealed class WorksController(WorkService service) : ControllerBase
{
    /// <summary>
    /// 上传儿童画并创建作品。
    /// </summary>
    [HttpPost("/api/works")]
    public Task<IResult> UploadDrawing() => service.UploadDrawing(HttpContext);

    /// <summary>
    /// 分页或按当前用户查询作品。
    /// </summary>
    [HttpGet("/api/works")]
    public Task<IResult> ListWorks() => service.ListWorks(HttpContext);

    /// <summary>
    /// 查询当前用户的指定作品。
    /// </summary>
    [HttpGet("/api/works/{id}")]
    public Task<IResult> GetWork([FromRoute] string id) => service.GetWork(id, HttpContext);

    /// <summary>
    /// 提交儿童画卡通形象生成任务。
    /// </summary>
    [HttpPost("/api/works/{id}/generate")]
    public Task<IResult> GenerateImage([FromRoute] string id) => service.GenerateImage(id, HttpContext);

    /// <summary>
    /// 确认候选形象并申请三维模型生成。
    /// </summary>
    [HttpPost("/api/works/{id}/select")]
    public Task<IResult> SelectImage([FromRoute] string id) => service.SelectImage(id, HttpContext);
}
