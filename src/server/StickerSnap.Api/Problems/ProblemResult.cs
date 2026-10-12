using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace StickerSnap.Api.Problems;

/// <summary>
/// A Problem answered from a controller: application/problem+json in the contract's JSON, with
/// its trace id, the same as the exception handler writes.
/// </summary>
public sealed class ProblemResult : IActionResult
{
    private readonly ProblemDetails _problem;

    public ProblemResult(ProblemDetails problem)
    {
        _problem = problem;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        http.Response.StatusCode = _problem.Status ?? StatusCodes.Status500InternalServerError;
        Problem.Complete(new ProblemDetailsContext { HttpContext = http, ProblemDetails = _problem });

        var json = http.RequestServices.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        await http.Response.WriteAsJsonAsync(_problem, json, "application/problem+json", http.RequestAborted);
    }
}
