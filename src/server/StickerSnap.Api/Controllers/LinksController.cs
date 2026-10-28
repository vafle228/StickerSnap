using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Links;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/links of group links: what a video by a YouTube or Giphy link is (artboard 20·3·4).</summary>
[ApiController]
[Route("api/v1/links")]
[Tags("links")]
public sealed class LinksController : ControllerBase
{
    private readonly ILinkService _service;

    public LinksController(ILinkService service)
    {
        _service = service;
    }

    /// <summary>
    /// The site, title, length, preview and expected size of the video; nothing is downloaded.
    /// Dropping the request is «Отменить» of «Узнаю о видео…».
    /// </summary>
    [HttpPost("probe", Name = "probeLink")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(LinkVideo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Probe([FromBody] LinkRequest request, CancellationToken cancellationToken) =>
        Ok(LinkVideo.From(await _service.ProbeAsync(request.Url, cancellationToken)));
}
