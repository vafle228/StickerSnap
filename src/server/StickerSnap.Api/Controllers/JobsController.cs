using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using JobService = StickerSnap.Application.Jobs.JobService;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/jobs of group jobs: previews, exports and publications run in the background. The
/// answer is the job as it is; how it goes on comes as the event jobUpdated.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Tags("jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly JobService _service;

    public JobsController(JobService service)
    {
        _service = service;
    }

    /// <summary>
    /// Starts a preview, an export or a publication. A new preview of the project cancels the one
    /// before; an export and a publication go by the saved revision; a second publication waits.
    /// </summary>
    [HttpPost(Name = "createJob")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Job), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Create([FromBody] JobRequest request, CancellationToken cancellationToken)
    {
        var job = await _service.CreateAsync(request.ToRequest(), cancellationToken);
        return Accepted(Job.From(job));
    }

    /// <summary>The jobs running, waiting and finished in the last 10 minutes, newest first: after F5.</summary>
    [HttpGet(Name = "listJobs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<Job>), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public IActionResult List() => Ok(_service.List().Select(Job.From).ToList());

    /// <summary>A job as it is now: the SPA polls it while the hub is down.</summary>
    [HttpGet("{jobId}", Name = "getJob")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Job), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public IActionResult Get(string jobId) => Ok(Job.From(_service.Get(jobId)));

    /// <summary>Cancels a job; stickers already sent stay in the pack.</summary>
    [HttpPost("{jobId}/cancel", Name = "cancelJob")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Job), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public IActionResult Cancel(string jobId) => Ok(Job.From(_service.Cancel(jobId)));

    /// <summary>«Повторить сейчас»: the segments a job left undone, if the project is still at its revision.</summary>
    [HttpPost("{jobId}/retry", Name = "retryJob")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Job), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Retry(string jobId, CancellationToken cancellationToken)
    {
        var job = await _service.RetryAsync(jobId, cancellationToken);
        return Accepted(Job.From(job));
    }
}
