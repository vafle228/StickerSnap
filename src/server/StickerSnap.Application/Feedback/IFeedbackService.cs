using StickerSnap.Application.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Feedback;

/// <summary>POST /api/v1/feedback: the window «Написать отзыв» (artboard 19·4).</summary>
public interface IFeedbackService
{
    /// <summary>
    /// Checks the draft, attaches the app's report to a bug and sends it to the team's inbox.
    /// </summary>
    /// <returns>The feedback's number, as the team sees it</returns>
    /// <exception cref="AppException">
    /// bad_request with fields text, contact, error or actions; what the inbox throws: network_unavailable,
    /// bot_unavailable, rate_limited
    /// </exception>
    Task<long> SendAsync(FeedbackDraft draft, CancellationToken cancellationToken);
}

/// <summary>What the user wrote in the window.</summary>
/// <param name="Text">Up to 2000 characters; empty only for a bug opened from an error</param>
/// <param name="CanReply">«Можно ответить мне в Telegram», or «Ответить мне на» without a link</param>
/// <param name="Contact">The e-mail or @username of «Ответить мне на», up to 100 characters</param>
/// <param name="Error">The error the window was opened from; only for a bug</param>
/// <param name="Actions">The interface's last steps, up to 30, the oldest first; only for a bug</param>
public sealed record FeedbackDraft(
    FeedbackKind Kind,
    string Text,
    bool CanReply,
    string? Contact = null,
    FeedbackError? Error = null,
    IReadOnlyList<InterfaceAction>? Actions = null);
