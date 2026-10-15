using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Publish;
using PublishDraft = StickerSnap.Api.Contracts.PublishDraft;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/projects/{id}/publish-draft of group publish: what the publish window opens with
/// (artboard 7). The sending itself is a job of kind publish (POST /api/v1/jobs).
/// </summary>
[ApiController]
[Route("api/v1/projects/{id:guid}/publish-draft")]
[Tags("publish")]
[Produces("application/json")]
public sealed class PublishController : ControllerBase
{
    private readonly IPublishDraftService _drafts;
    private readonly IJobService _jobs;

    public PublishController(IPublishDraftService drafts, IJobService jobs)
    {
        _drafts = drafts;
        _jobs = jobs;
    }

    /// <summary>The segments with their exports and where they were sent, the last pack, the packs, the recent emoji.</summary>
    [HttpGet(Name = "getPublishDraft")]
    [ProducesResponseType(typeof(PublishDraft), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(PublishDraft.From(await _drafts.GetAsync(id, cancellationToken), _jobs.List()));
}
