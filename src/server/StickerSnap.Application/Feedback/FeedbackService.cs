using System.Text;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Feedback;

/// <summary>
/// The window «Написать отзыв»: a bug always carries the app's report (there is no checkbox for it,
/// artboard 19·4), an idea or anything else carries only what the user wrote.
/// </summary>
public sealed class FeedbackService(IFeedbackInbox inbox, IDiagnosticsCollector diagnostics) : IFeedbackService
{
    public const int TextLimit = 2000;
    public const int ContactLimit = 100;
    public const int CodeLimit = 64;

    /// <summary>The service takes up to 200 KB of the report in UTF-8.</summary>
    public const int DiagnosticsLimitBytes = 204_800;

    /// <inheritdoc />
    public async Task<long> SendAsync(FeedbackDraft draft, CancellationToken cancellationToken)
    {
        var text = draft.Text.Trim();
        var contact = draft.Contact?.Trim() is { Length: > 0 } given ? given : null;
        Check(draft, text, contact);

        var report = draft.Kind == FeedbackKind.Bug
            ? Cut(await diagnostics.CollectAsync(draft.Error, cancellationToken))
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
            else if (error.Code.Length is 0 or > CodeLimit || error.TraceId is { Length: > CodeLimit }) fields["error"] = ["invalid"];
        }

        if (fields.Count > 0)
        {
            throw new AppException(ErrorCode.BadRequest, $"Feedback is wrong in {string.Join(", ", fields.Keys)}")
            {
                Fields = fields,
            };
        }
    }

    /// <summary>Keeps the head of a report over the limit: versions and the error come first.</summary>
    private static string Cut(string report)
    {
        if (Encoding.UTF8.GetByteCount(report) <= DiagnosticsLimitBytes) return report;
        // A UTF-16 character takes up to 3 bytes: a third of the excess at a time never cuts past the limit by much
        var chars = Math.Min(report.Length, DiagnosticsLimitBytes);
        int bytes;
        while ((bytes = Encoding.UTF8.GetByteCount(report.AsSpan(0, chars))) > DiagnosticsLimitBytes)
        {
            chars -= (bytes - DiagnosticsLimitBytes + 2) / 3;
        }
        if (chars > 0 && char.IsHighSurrogate(report[chars - 1])) chars--;
        return report[..chars];
    }
}
