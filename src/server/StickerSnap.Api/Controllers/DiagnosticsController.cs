using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Diagnostics;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/diagnostics of group feedback: «Что отправится» of the window 19·4.</summary>
[ApiController]
[Route("api/v1/diagnostics")]
[Tags("feedback")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IDiagnosticsService _service;

    public DiagnosticsController(IDiagnosticsService service)
    {
        _service = service;
    }

    /// <summary>The report a bug goes with: versions, the error of the trace with its context, the server's log.</summary>
    [HttpGet(Name = "getDiagnostics")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(DiagnosticsReportBody), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get([FromQuery] string? traceId, CancellationToken cancellationToken)
    {
        var report = await _service.CollectAsync(new ReportSubject(traceId), cancellationToken);
        return Ok(DiagnosticsReportBody.From(report));
    }
}
