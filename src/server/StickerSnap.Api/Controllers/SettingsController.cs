using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Settings;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/settings of group app.</summary>
[ApiController]
[Route("api/v1/settings")]
[Tags("app")]
[Produces("application/json")]
public sealed class SettingsController : ControllerBase
{
    private readonly SettingsService _service;

    public SettingsController(SettingsService service)
    {
        _service = service;
    }

    /// <summary>The theme, the accent, the two folders and the recent emoji.</summary>
    [HttpGet(Name = "getSettings")]
    [ProducesResponseType(typeof(Settings), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await _service.GetAsync(cancellationToken);
        return Ok(Settings.From(settings));
    }

    /// <summary>Changes one or more fields at once, without «Сохранить»; sends settingsChanged.</summary>
    [HttpPatch(Name = "patchSettings")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Settings), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Patch([FromBody] SettingsPatch patch, CancellationToken cancellationToken)
    {
        var settings = await _service.UpdateAsync(patch.ToChange(), cancellationToken);
        return Ok(Settings.From(settings));
    }
}
