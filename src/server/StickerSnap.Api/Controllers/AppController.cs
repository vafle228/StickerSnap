using Microsoft.AspNetCore.Mvc;
using StickerSnap.Application.App;
using AppInfo = StickerSnap.Api.Contracts.AppInfo;

namespace StickerSnap.Api.Controllers;

/// <summary>GET /api/v1/app of group app.</summary>
[ApiController]
[Route("api/v1/app")]
[Tags("app")]
[Produces("application/json")]
public sealed class AppController : ControllerBase
{
    private readonly AppInfoService _service;

    public AppController(AppInfoService service)
    {
        _service = service;
    }

    /// <summary>Versions, the mode, the bot and the limits of Telegram.</summary>
    [HttpGet(Name = "getApp")]
    [ProducesResponseType(typeof(AppInfo), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var app = await _service.GetAsync(cancellationToken);
        return Ok(AppInfo.From(app));
    }
}
