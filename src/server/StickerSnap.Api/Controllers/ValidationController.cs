using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Validation;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/validate of group sources.</summary>
[ApiController]
[Route("api/v1/validate")]
[Tags("sources")]
[Produces("application/json")]
public sealed class ValidationController : ControllerBase
{
    private readonly ValidationService _service;

    public ValidationController(ValidationService service)
    {
        _service = service;
    }

    /// <summary>Checks any .webm against every rule of Telegram's table (R8): quick, synchronous.</summary>
    [HttpPost(Name = "validateWebm")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ValidationReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Validate([FromBody] SourceRequest request, CancellationToken cancellationToken)
    {
        var validation = await _service.ValidateAsync(request.Source.ToSource(), cancellationToken);
        return Ok(ValidationReport.From(validation));
    }
}
