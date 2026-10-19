namespace StickerSnap.Application.Feedback;

/// <summary>
/// Where feedback from the app goes: the team reads it, and answers if the user allowed it.
/// Works whether or not the computer is linked; a linked computer sends it on its user's behalf.
/// Which service takes it is decided by the registration in StickerSnap.Infrastructure.
/// </summary>
/// <remarks>
/// A failure is an <see cref="Core.Errors.AppException"/>: network_unavailable, bot_unavailable
/// (the service is down or not configured), rate_limited (params.retryAfterSec).
/// </remarks>
public interface IFeedbackInbox
{
    /// <returns>The feedback's number, as the team sees it</returns>
    Task<long> SendAsync(FeedbackLetter letter, CancellationToken cancellationToken);
}

/// <summary>«Ошибка», «Идея», «Другое».</summary>
public enum FeedbackKind
{
    Bug,
    Idea,
    Other,
}

/// <param name="Text">Up to 2000 characters; empty only for a <see cref="FeedbackKind.Bug"/> with an <paramref name="Error"/></param>
/// <param name="CanReply">«Можно ответить мне»</param>
/// <param name="Contact">
/// An e-mail or @username to answer at, for a computer that is not linked; a linked one is answered
/// in the messenger, so the contact is left out
/// </param>
/// <param name="Error">The error the feedback was opened from; only for a bug</param>
/// <param name="Diagnostics">The app's report of what went wrong, up to 200 KB; only for a bug</param>
public sealed record FeedbackLetter(
    FeedbackKind Kind,
    string Text,
    bool CanReply,
    string? Contact = null,
    FeedbackError? Error = null,
    string? Diagnostics = null);

/// <param name="Code">The error's code of the contract, such as render_failed</param>
/// <param name="TraceId">The trace id the error was shown with</param>
public sealed record FeedbackError(string Code, string? TraceId);
