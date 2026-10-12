using StickerSnap.Core.Errors;

namespace StickerSnap.Core.Jobs;

/// <summary>Why a job or one of its segments failed: what the SPA shows, as an <see cref="AppException"/> carries it.</summary>
/// <param name="Message">English, for the log</param>
/// <param name="Log">The tail of the media engine's log, under «Подробности»</param>
public sealed record JobError(
    ErrorCode Code,
    string Message,
    IReadOnlyDictionary<string, object> Params,
    IReadOnlyDictionary<string, string[]> Fields,
    string? Log)
{
    public static JobError Of(AppException exception) =>
        new(exception.Code, exception.Message, exception.Params, exception.Fields, exception.Log);

    public static JobError Of(ErrorCode code, string message) =>
        new(code, message, new Dictionary<string, object>(), new Dictionary<string, string[]>(), null);
}
