using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Problems;

/// <summary>
/// The journal's record of an answered problem: the route it came from with the project, segment
/// and pack it names, and the exception behind it.
/// </summary>
public static class ProblemRecords
{
    public static ErrorRecord Of(HttpContext http, ProblemDetails problem, ErrorCode code, Exception? exception, string traceId)
    {
        var at = (http.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        // The exception handler runs with the endpoint cleared: the one that threw is in its feature
        var thrown = http.Features.Get<IExceptionHandlerFeature>();
        var endpoint = thrown?.Endpoint ?? http.GetEndpoint();
        var values = thrown?.RouteValues ?? http.GetRouteData().Values;
        var template = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        var operation = $"{http.Request.Method} {(template is null ? http.Request.Path.Value : "/" + template.TrimStart('/'))}";

        var where = new Dictionary<string, string>(StringComparer.Ordinal);
        if (template?.Contains("projects/{id", StringComparison.Ordinal) == true && values["id"] is { } project)
            where[ErrorRecord.ProjectKey] = project.ToString()!;
        if (values["segmentId"] is { } segment) where[ErrorRecord.SegmentKey] = segment.ToString()!;
        if (values["pack"] is { } pack) where[ErrorRecord.PackKey] = pack.ToString()!;

        return exception is null
            ? new ErrorRecord(traceId, at, code, problem.Detail ?? problem.Title ?? code.ToString(), operation, where)
            : ErrorRecord.Of(exception, traceId, at, operation, where) with { Code = code };
    }
}
