using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using StickerSnap.Api.Contracts;
using StickerSnap.Api.Json;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/uploads of group sources: a browser's file, before a project refers to it.</summary>
[ApiController]
[Route("api/v1/uploads")]
[Tags("sources")]
public sealed class UploadsController : ControllerBase
{
    private const string FileField = "file";

    private readonly IUploadService _service;

    public UploadsController(IUploadService service)
    {
        _service = service;
    }

    /// <summary>
    /// Takes the `file` part of a multipart body as it arrives, straight to the data directory; the
    /// service sets the limit. A dropped request leaves no file.
    /// </summary>
    [HttpPost(Name = "createUpload")]
    [MultipartFile(FileField)]
    [DisableRequestSizeLimit]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Upload), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Create()
    {
        // No parameters at all: binding one would make MVC read the whole form before the action runs
        var cancellationToken = HttpContext.RequestAborted;
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var type) ||
            !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
            HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
            throw NoFile("The body is not multipart/form-data");

        var reader = new MultipartReader(boundary, Request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !disposition.IsFileDisposition() ||
                HeaderUtilities.RemoveQuotes(disposition.Name).Value != FileField)
                continue;

            var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar : HeaderUtilities.RemoveQuotes(disposition.FileName);
            var uploadId = await _service.UploadAsync(name.Value ?? "", section.Body, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new Upload(uploadId));
        }

        throw NoFile("The body has no file part");
    }

    private static AppException NoFile(string message) => new(ErrorCode.BadRequest, message)
    {
        Fields = new Dictionary<string, string[]> { [FileField] = ["required"] },
    };
}
