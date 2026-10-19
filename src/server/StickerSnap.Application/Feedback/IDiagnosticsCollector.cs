namespace StickerSnap.Application.Feedback;

/// <summary>
/// The report for the developer that goes with every bug: what the app is and runs on, and what it
/// knows about the error. Never videos, frames, projects, keys, full paths or the system's user name.
/// </summary>
public interface IDiagnosticsCollector
{
    /// <param name="error">The error the feedback was opened from, if any</param>
    /// <returns>Plain text for a person to read</returns>
    Task<string> CollectAsync(FeedbackError? error, CancellationToken cancellationToken);
}
