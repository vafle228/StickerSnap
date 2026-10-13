using Microsoft.AspNetCore.Mvc;
using StickerSnap.Application.Files;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/files of group media: the drafts of the exact preview and the exported stickers.</summary>
[ApiController]
[Route("api/v1/files")]
[Tags("media")]
public sealed class FilesController : ControllerBase
{
    private const string Webm = "video/webm";

    private readonly IFileService _service;

    public FilesController(IFileService service)
    {
        _service = service;
    }

    /// <summary>
    /// A rendered .webm: the SPA plays a draft from memory and downloads an export in the browser
    /// (R9). A Range asks for part of it.
    /// </summary>
    [HttpGet("{fileId}", Name = "getFile")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, Webm)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(string fileId, CancellationToken cancellationToken)
    {
        var path = await _service.PathOfAsync(fileId, cancellationToken);
        return PhysicalFile(path, Webm, enableRangeProcessing: true);
    }
}
