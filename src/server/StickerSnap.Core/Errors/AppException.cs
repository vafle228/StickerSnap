namespace StickerSnap.Core.Errors;

/// <summary>
/// A failure the SPA knows how to show: its <see cref="Code"/> and what the words for it need.
/// Any layer throws it; the API turns it into Problem Details with the code's HTTP status.
/// The message is English for the log and never reaches the interface.
/// </summary>
public sealed class AppException(ErrorCode code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorCode Code { get; } = code;

    /// <summary>What the phrase is filled with: { ext: "avi" }, { retryAfterSec: 24 }. Strings or numbers.</summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } = new Dictionary<string, object>();

    /// <summary>Errors of request fields, for a hint under the field: { "folders.projects": ["not_absolute"] }.</summary>
    public IReadOnlyDictionary<string, string[]> Fields { get; init; } = new Dictionary<string, string[]>();

    /// <summary>What the media engine said, such as the tail of its log, shown under «Подробности».</summary>
    public string? Log { get; init; }

    /// <summary>
    /// What the app was doing when it failed, for the developer's report and never the interface:
    /// { command: "ffmpeg -i …" }, { method: "createNewStickerSet", answer: "…" }.
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}
