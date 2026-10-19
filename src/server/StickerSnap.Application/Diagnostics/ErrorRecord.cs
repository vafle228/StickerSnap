using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// One error the server answered or a job ended with, as the developer's report tells it.
/// </summary>
/// <param name="TraceId">The number the SPA was shown: a request's trace id, or the job's id</param>
/// <param name="Operation">What was going on: `POST /api/v1/projects/{id}/export`, `export job`</param>
/// <param name="Context">
/// Where and how: project, segment, pack; the media engine's command, a service's method and answer
/// </param>
/// <param name="Log">What the media engine or a service said, as «Подробности» show it</param>
/// <param name="Exception">The stack of an exception nobody expected</param>
public sealed record ErrorRecord(
    string TraceId,
    DateTimeOffset At,
    ErrorCode Code,
    string Message,
    string Operation,
    IReadOnlyDictionary<string, string> Context,
    string? Log = null,
    string? Exception = null)
{
    /// <summary>The project the error was with, when its context names one.</summary>
    public const string ProjectKey = "project";

    public const string SegmentKey = "segment";

    public const string PackKey = "pack";

    /// <summary>
    /// The record of a thrown error: an <see cref="AppException"/> with its code, log and context,
    /// anything else as `internal` with its stack.
    /// </summary>
    /// <param name="where">The context the caller knows, such as the project; the exception's own context wins</param>
    public static ErrorRecord Of(
        Exception exception,
        string traceId,
        DateTimeOffset at,
        string operation,
        IReadOnlyDictionary<string, string> where)
    {
        var context = new Dictionary<string, string>(where, StringComparer.Ordinal);
        if (exception is not AppException app)
            return new ErrorRecord(traceId, at, ErrorCode.Internal, exception.Message, operation, context, Exception: exception.ToString());

        foreach (var (key, value) in app.Context) context[key] = value;
        return new ErrorRecord(
            traceId, at, app.Code, app.Message, operation, context, app.Log, app.InnerException?.ToString());
    }
}
