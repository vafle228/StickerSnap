using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using StickerSnap.Application.ProjectMedia;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/projects/{id}/… of group media: what the browser loads by itself for a project — the
/// editor's video, the card's cover, the timeline's frames. Each answer carries an ETag of its
/// content and is revalidated on every use, so a replaced video or an edited first sticker shows at once.
/// </summary>
[ApiController]
[Route("api/v1/projects/{id:guid}")]
[Tags("media")]
public sealed class ProjectMediaController : ControllerBase
{
    private const string Webp = "image/webp";
    private const string Mp4 = "video/mp4";
    private const string Webm = "video/webm";

    private readonly IProjectMediaService _service;

    public ProjectMediaController(IProjectMediaService service)
    {
        _service = service;
    }

    /// <summary>The card's cover: the first sticker's middle frame, as WEBP.</summary>
    [HttpGet("cover", Name = "getProjectCover")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, Webp)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Cover(Guid id, CancellationToken cancellationToken) =>
        Serve(await _service.CoverAsync(id, cancellationToken));

    /// <summary>
    /// The editor's &lt;video&gt;: the source as it is when the browser plays it, else a WEBM with the
    /// same frames (GIF, WEBP, MOV, a variable rate). A Range asks for part of it.
    /// </summary>
    /// <param name="range">Read by the file answer itself; named here for the contract</param>
    [HttpGet("media", Name = "getProjectMedia")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, Mp4, Webm)]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status206PartialContent, Mp4, Webm)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Media(Guid id, [FromHeader(Name = "Range")] string? range, CancellationToken cancellationToken) =>
        Serve(await _service.VideoAsync(id, cancellationToken));

    /// <summary>
    /// The timeline's «Кадры»: <paramref name="count"/> frames from <paramref name="from"/> in one row,
    /// each <paramref name="height"/> px tall, as one WEBP picture.
    /// </summary>
    /// <param name="from">The source's first frame of the strip</param>
    /// <param name="count">How many frames, at most 120</param>
    /// <param name="height">Of a frame, px, at most 720</param>
    [HttpGet("frames", Name = "getProjectFrames")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, Webp)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Frames(
        Guid id,
        [FromQuery, Required] int? from,
        [FromQuery, Required] int? count,
        [FromQuery, Required] int? height,
        CancellationToken cancellationToken) =>
        Serve(await _service.FramesAsync(id, from!.Value, count!.Value, height!.Value, cancellationToken));

    private PhysicalFileResult Serve(MediaFile file)
    {
        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(
            file.Path,
            file.ContentType,
            lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{file.Tag}\""),
            enableRangeProcessing: true);
    }
}
