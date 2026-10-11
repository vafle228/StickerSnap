using Microsoft.AspNetCore.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Problems;

/// <summary>
/// Turns a thrown <see cref="AppException"/> into its Problem; a request the framework could not
/// read is `bad_request`, and any other exception is `internal` with the details left in the log.
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            AppException app => Problem.Of(app),
            BadHttpRequestException bad => Problem.Of(ErrorCode.BadRequest, bad.Message),
            _ => Problem.Of(ErrorCode.Internal),
        };
        if (exception is not (AppException or BadHttpRequestException))
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
