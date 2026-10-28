using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Links;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/downloads of group links: a video by link is downloaded and becomes a project
/// (artboards 20·3·5–20·4). The answer is the download as it is; how it goes on comes as the
/// event downloadUpdated.
/// </summary>
[ApiController]
[Route("api/v1/downloads")]
[Tags("links")]
public sealed class DownloadsController : ControllerBase
{
    private readonly IDownloadService _service;

    public DownloadsController(IDownloadService service)
    {
        _service = service;
    }

    /// <summary>Downloads the video of the link and makes a project of it; a second download waits for the first.</summary>
    [HttpPost(Name = "createDownload")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Download), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Create([FromBody] LinkRequest request, CancellationToken cancellationToken) =>
        Accepted(Download.From(await _service.CreateAsync(request.Url, cancellationToken)));

    /// <summary>The downloads running and finished in the last 10 minutes, newest first: after F5.</summary>
    [HttpGet(Name = "listDownloads")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<Download>), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public IActionResult List() => Ok(_service.List().Select(Download.From).ToList());

    /// <summary>«Отменить»: the file downloaded so far is removed.</summary>
    [HttpPost("{downloadId}/cancel", Name = "cancelDownload")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Download), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public IActionResult Cancel(string downloadId) => Ok(Download.From(_service.Cancel(downloadId)));
}
