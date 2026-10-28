using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Packs;
using PackNameCheck = StickerSnap.Api.Contracts.PackNameCheck;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/packs of group packs: «Мои паки» (artboard 7), «Добавить по ссылке», the name of a new
/// pack, and the stickers' files, which come through the server so the bot's token stays here.
/// </summary>
[ApiController]
[Route("api/v1/packs")]
[Tags("packs")]
public sealed class PacksController : ControllerBase
{
    private const string Webm = "video/webm";

    private readonly IPackService _service;
    private readonly IJobService _jobs;

    public PacksController(IPackService service, IJobService jobs)
    {
        _service = service;
        _jobs = jobs;
    }

    /// <summary>The packs the app knows, without asking Telegram.</summary>
    [HttpGet(Name = "listPacks")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<PackSummary>), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var packs = await _service.ListAsync(cancellationToken);
        return Ok(packs.Select(pack => PackSummary.From(pack, _jobs.List())).ToList());
    }

    /// <summary>«Добавить по ссылке»: a pack of @StickerSnapAppBot onto the list; another bot's is 422 not_our_pack.</summary>
    [HttpPost("import", Name = "importPack")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Pack), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Import([FromBody] ImportPackRequest request, CancellationToken cancellationToken) =>
        Ok(Pack.From(await _service.ImportAsync(request.Link, cancellationToken), _jobs.List()));

    /// <summary>«Скопировать в StickerSnap»: any pack copied into a new pack of @StickerSnapAppBot, then onto the list.</summary>
    [HttpPost("copy", Name = "copyPack")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Pack), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Copy([FromBody] CopyPackRequest request, CancellationToken cancellationToken) =>
        Ok(Pack.From(await _service.CopyAsync(request.Link, request.Title, cancellationToken), _jobs.List()));

    /// <summary>The link of a new pack from its title, what Telegram would refuse, and «Взять kot_divan_2».</summary>
    [HttpPost("name-check", Name = "checkPackName")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PackNameCheck), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> CheckName([FromBody] PackNameCheckRequest request, CancellationToken cancellationToken) =>
        Ok(PackNameCheck.From(await _service.CheckNameAsync(request.Title, request.Name, cancellationToken)));

    /// <summary>The pack with its stickers, their emoji and the project each came from.</summary>
    /// <param name="pack">The whole name, with _by_StickerSnapAppBot</param>
    [HttpGet("{pack}", Name = "getPack")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Pack), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(string pack, CancellationToken cancellationToken) =>
        Ok(Pack.From(await _service.GetAsync(pack, cancellationToken), _jobs.List()));

    /// <summary>«Убрать из списка»: nothing is deleted in Telegram.</summary>
    /// <param name="pack">The whole name, with _by_StickerSnapAppBot</param>
    [HttpDelete("{pack}", Name = "deletePack")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Delete(string pack, CancellationToken cancellationToken)
    {
        await _service.ForgetAsync(pack, cancellationToken);
        return NoContent();
    }

    /// <summary>A check with Telegram (getStickerSet), on opening a pack and by «обновить»; sends packChanged.</summary>
    /// <param name="pack">The whole name, with _by_StickerSnapAppBot</param>
    [HttpPost("{pack}/sync", Name = "syncPack")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Pack), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Sync(string pack, CancellationToken cancellationToken) =>
        Ok(Pack.From(await _service.SyncAsync(pack, cancellationToken), _jobs.List()));

    /// <summary>The sticker out of the pack in Telegram, for everyone who added it; sends packChanged.</summary>
    /// <param name="pack">The whole name, with _by_StickerSnapAppBot</param>
    /// <param name="stickerId">file_unique_id from Telegram</param>
    [HttpDelete("{pack}/stickers/{stickerId}", Name = "deletePackSticker")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> DeleteSticker(string pack, string stickerId, CancellationToken cancellationToken)
    {
        await _service.DeleteStickerAsync(pack, stickerId, cancellationToken);
        return NoContent();
    }

    /// <summary>The sticker's file for its tile (.webm, or .webp and .tgs in a copied pack), downloaded from Telegram once and kept by the server.</summary>
    /// <param name="pack">The whole name, with _by_StickerSnapAppBot</param>
    /// <param name="stickerId">file_unique_id from Telegram</param>
    [HttpGet("{pack}/stickers/{stickerId}/media", Name = "getPackStickerMedia")]
    [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK, Webm, "image/webp", "application/x-tgsticker")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> StickerMedia(string pack, string stickerId, CancellationToken cancellationToken)
    {
        var file = await _service.StickerMediaAsync(pack, stickerId, cancellationToken);
        // The same id is the same file for good
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return PhysicalFile(file.Path, file.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"{file.Tag}\""));
    }
}
