using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Problems;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// What no other route takes under /api and /hubs. An API path without a handler is a mistake of
/// the SPA: bad_request, never the SPA page. An unknown hub is a plain 404, as SignalR answers.
/// Order 1 puts them after every real route, and before the SPA page, the fallback of all paths.
/// Not an [ApiController]: that would turn the bare 404 into a Problem without a code.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class FallbackController : ControllerBase
{
    [Route("api/{**path}", Order = 1)]
    public IActionResult Api()
    {
        var problem = Problems.Problem.Of(ErrorCode.BadRequest, $"No endpoint for {Request.Method} {Request.Path}");
        return new ProblemResult(problem);
    }

    [Route("hubs/{**path}", Order = 1)]
    public IActionResult Hub()
    {
        return new StatusCodeResult(StatusCodes.Status404NotFound);
    }
}
