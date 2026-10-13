using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Estimates;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/projects/{id}/estimate of group media.</summary>
[ApiController]
[Route("api/v1/projects/{id:guid}/estimate")]
[Tags("media")]
[Produces("application/json")]
public sealed class EstimateController : ControllerBase
{
    private readonly IEstimateService _service;

    public EstimateController(IEstimateService service)
    {
        _service = service;
    }

    /// <summary>
    /// What a segment as the editor has it, saved or not, will weigh, with what the export will
    /// likely give up and the edits that would bring it under the limit (3a «Экспорт», 3c G).
    /// </summary>
    [HttpPost(Name = "estimateSegment")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Estimate), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Estimate(Guid id, [FromBody] EstimateRequest request, CancellationToken cancellationToken)
    {
        var estimate = await _service.EstimateAsync(id, request.Segment.ToSegment(), cancellationToken);
        return Ok(Contracts.Estimate.From(estimate));
    }
}
