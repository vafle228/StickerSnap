using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Problems;

/// <summary>
/// Turns a thrown <see cref="AppException"/> into its Problem; a request the framework could not
/// read is `bad_request`, and any other exception is `internal` with the details left in the log.
/// A body System.Text.Json refuses rather than fails to parse, such as a crop without its `mode`,
/// is not read either: the input formatter turns only broken JSON into a 400 by itself.
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
            NotSupportedException unreadable when IsUnreadableBody(context, unreadable) =>
                Problem.Of(ErrorCode.BadRequest, unreadable.Message),
            _ => Problem.Of(ErrorCode.Internal),
        };
        if (problem.Status >= StatusCodes.Status500InternalServerError && exception is not AppException)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>The serializer refused the body of the request before any answer was written.</summary>
    private static bool IsUnreadableBody(HttpContext context, NotSupportedException exception) =>
        exception.TargetSite?.DeclaringType?.Assembly == typeof(JsonSerializer).Assembly &&
        context.Request.HasJsonContentType() &&
        !context.Response.HasStarted;
}
