using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.Core;

namespace Tongyin.Api.Controllers;

// Infrastructure-level endpoints; all API routes now use MVC controllers.
[ApiController]
public sealed class SystemController(Settings settings, IStore store) : ControllerBase
{
    [HttpGet("/health")]
    public IResult Health() => Results.Json(new { status = "ok", backend = "dotnet" });

    [HttpGet("/api/config")]
    public async Task<IResult> Config() => Results.Json(new
    {
        price_fen = settings.PriceFen,
        dev = settings.DevMode,
        ai_auto_enabled = await store.Read(s => s.AIProviders.Values.Any(p => Doc.Bool(p,"default") && Doc.Bool(p,"enabled") && Doc.Str(p,"stage")=="image")) || settings.AiWorkerUrl.Length > 0,
        free_image_attempts = settings.FreeImageAttempts
    });
}
