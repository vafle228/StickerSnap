using StickerSnap.Application.Diagnostics;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Feedback;

/// <summary>
/// The window «Написать отзыв»: a bug always carries the app's report (there is no checkbox for it,
/// artboard 19·4), an idea or anything else carries only what the user wrote.
/// </summary>
public sealed class FeedbackService(IFeedbackInbox inbox, IDiagnosticsService diagnostics) : IFeedbackService
{
    public const int TextLimit = 2000;
    public const int ContactLimit = 100;
    public const int CodeLimit = 64;
    public const int MessageLimit = 1000;
    public const int StackLimit = 8000;
    public const int CountLimit = 1000;
    public const int ActionsLimit = 30;
    public const int ActionLimit = 200;

    /// <inheritdoc />
    public async Task<long> SendAsync(FeedbackDraft draft, CancellationToken cancellationToken)
    {
        var text = draft.Text.Trim();
        var contact = draft.Contact?.Trim() is { Length: > 0 } given ? given : null;
        Check(draft, text, contact);

        var report = draft.Kind == FeedbackKind.Bug
            ? (await diagnostics.CollectAsync(SubjectOf(draft), cancellationToken)).Text
            : null;
        var letter = new FeedbackLetter(
            draft.Kind,
            text,
            draft.CanReply,
            draft.CanReply ? contact : null,
            draft.Error,
            report);
        return await inbox.SendAsync(letter, cancellationToken);
    }

    private static void Check(FeedbackDraft draft, string text, string? contact)
    {
        var fields = new Dictionary<string, string[]>();
        if (text.Length > TextLimit) fields["text"] = ["too_long"];
        else if (text.Length == 0 && !(draft.Kind == FeedbackKind.Bug && draft.Error is not null)) fields["text"] = ["empty"];
        if (contact is { Length: > ContactLimit }) fields["contact"] = ["too_long"];
        if (draft.Error is { } error)
        {
            if (draft.Kind != FeedbackKind.Bug) fields["error"] = ["bug_only"];
            else if (!Valid(error)) fields["error"] = ["invalid"];
        }
        if (draft.Actions is { Count: > 0 } actions)
        {
            if (draft.Kind != FeedbackKind.Bug) fields["actions"] = ["bug_only"];
            else if (actions.Count > ActionsLimit) fields["actions"] = ["too_many"];
            else if (actions.Any(action => action.What.Length > ActionLimit)) fields["actions"] = ["too_long"];
        }

        if (fields.Count > 0)
        {
            throw new AppException(ErrorCode.BadRequest, $"Feedback is wrong in {string.Join(", ", fields.Keys)}")
            {
                Fields = fields,
            };
        }
    }

    private static bool Valid(FeedbackError error) =>
        error.Code.Length is > 0 and <= CodeLimit
        && error.TraceId is not { Length: > CodeLimit }
        && error.Message is not { Length: > MessageLimit }
        && error.Stack is not { Length: > StackLimit }
        && error.Count is >= 1 and <= CountLimit;

    private static ReportSubject SubjectOf(FeedbackDraft draft) => draft.Error is { } error
        ? new ReportSubject(error.TraceId, error.Code, error.Source, error.Message, error.Stack, error.At, error.Count, draft.Actions)
        : new ReportSubject(Actions: draft.Actions);
}
