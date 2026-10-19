using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Feedback;

namespace StickerSnap.Api.Controllers;

/// <summary>/api/v1/feedback of group feedback: the window «Написать отзыв» (artboard 19·4).</summary>
[ApiController]
[Route("api/v1/feedback")]
[Tags("feedback")]
public sealed class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _service;

    public FeedbackController(IFeedbackService service)
    {
        _service = service;
    }

    /// <summary>Sends feedback to the team; a bug goes with the app's report the server collects.</summary>
    [HttpPost(Name = "sendFeedback")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(FeedbackCreated), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Send([FromBody] FeedbackRequest request, CancellationToken cancellationToken)
    {
        var id = await _service.SendAsync(request.ToDraft(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new FeedbackCreated(id));
    }
}
