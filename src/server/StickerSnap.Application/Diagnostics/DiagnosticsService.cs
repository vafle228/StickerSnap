using System.Globalization;
using System.Text;
using System.Text.Json;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// The report of a bug (contract, getDiagnostics): its head is the app, the error and its project;
/// the server's log fills the rest of <see cref="LimitBytes"/>, the newest entries kept first.
/// </summary>
public sealed class DiagnosticsService(
    ISystemSummary system,
    IErrorJournal journal,
    ILogArchive log,
    IProjectService projects,
    TimeProvider clock) : IDiagnosticsService
{
    public const int TraceIdLimit = 64;

    /// <summary>The service takes up to 200 KB of the report in UTF-8.</summary>
    public const int LimitBytes = 204_800;

    /// <summary>The latest errors other than the one reported, one line each.</summary>
    public const int LatestErrors = 10;

    /// <summary>How much of the log before the error a report takes (artboard 19·4б).</summary>
    public static readonly TimeSpan LogBeforeError = TimeSpan.FromMinutes(15);

    /// <summary>The same error again within this window is one report with a count (artboard 19·4).</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(10);

    /// <summary>How much of the log a report takes when it knows of no error.</summary>
    public static readonly TimeSpan LogWithoutError = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<DiagnosticsReport> CollectAsync(ReportSubject subject, CancellationToken cancellationToken)
    {
        var traceId = subject.TraceId?.Trim() is { Length: > 0 } given ? given : null;
        if (traceId is { Length: > TraceIdLimit })
        {
            throw new AppException(ErrorCode.BadRequest, $"A trace id is up to {TraceIdLimit} characters")
            {
                Fields = new Dictionary<string, string[]> { ["traceId"] = ["too_long"] },
            };
        }

        var now = clock.GetUtcNow();
        var server = subject.Source == ErrorSource.Server;
        var head = new StringBuilder();
        foreach (var line in await system.DescribeAsync(cancellationToken)) head.Append(line).Append('\n');
        head.Append(CultureInfo.InvariantCulture, $"Sent at {Stamp(now)}\n");
        if (subject.Code is not null || traceId is not null)
        {
            var number = traceId is null ? null : server ? $"trace {traceId}" : $"number {traceId}";
            head.Append("Error: ").AppendJoin(", ", new[] { subject.Code, number }.OfType<string>());
            if (!server) head.Append(CultureInfo.InvariantCulture, $" ({Snake(subject.Source)})");
            head.Append('\n');
        }
        if (subject.Count > 1)
            head.Append(CultureInfo.InvariantCulture, $"Repeated {subject.Count} times in {RepeatWindow.TotalMinutes} minutes\n");

        // An error of the interface or the shell never reached the server: its journal has no trace of it
        var errors = traceId is null || !server ? [] : journal.Find(traceId);
        if (traceId is not null && server && errors.Count == 0)
        {
            head.Append(CultureInfo.InvariantCulture,
                $"Trace {traceId} is not in memory: the server keeps its last {ErrorJournal.Capacity} errors until it restarts\n");
        }
        if (!server) DescribeOwn(head, subject);
        foreach (var error in errors) Describe(head, error);
        foreach (var project in errors.Select(ProjectOf).OfType<Guid>().Distinct())
            await DescribeProjectAsync(head, project, cancellationToken);

        var latest = journal.Latest(LatestErrors).Where(error => error.TraceId != traceId).ToList();
        if (latest.Count > 0)
        {
            head.Append("\n== Latest errors ==\n");
            foreach (var error in latest)
            {
                head.Append(CultureInfo.InvariantCulture,
                    $"{Stamp(error.At)} {Snake(error.Code)} in {error.Operation}, trace {error.TraceId}\n");
            }
        }

        if (subject.Actions is { Count: > 0 } actions)
        {
            head.Append("\n== Last actions in the interface ==\n");
            foreach (var action in actions)
                head.Append(CultureInfo.InvariantCulture, $"{Stamp(action.At)} {action.What.ReplaceLineEndings(" ")}\n");
        }

        var since = errors.Count > 0 ? errors.Min(error => error.At) - LogBeforeError
            : subject.At is { } at && at <= now ? at - LogBeforeError
            : now - LogWithoutError;
        var entries = await log.ReadAsync(since, cancellationToken);

        var scrubber = new ReportScrubber(system.UserNames, await system.SecretsAsync(cancellationToken));
        var text = Fit(scrubber.Scrub(head.ToString()), [.. entries.Select(scrubber.Scrub)], since);
        return new DiagnosticsReport(text, Encoding.UTF8.GetByteCount(text));
    }

    /// <summary>What the interface or the shell knows of an error the server never saw.</summary>
    private static void DescribeOwn(StringBuilder report, ReportSubject subject)
    {
        var title = subject.Source == ErrorSource.Shell ? "Last launch" : "Interface error";
        report.Append(CultureInfo.InvariantCulture, $"\n== {title}");
        if (subject.At is { } at) report.Append(CultureInfo.InvariantCulture, $" at {Stamp(at)}");
        report.Append(" ==\n");
        if (subject.Message is { Length: > 0 } message) report.Append("Message: ").Append(Indent(message)).Append('\n');
        if (subject.Stack is { Length: > 0 } stack) report.Append("Stack:\n  ").Append(Indent(stack.TrimEnd())).Append('\n');
    }

    private static void Describe(StringBuilder report, ErrorRecord error)
    {
        report.Append(CultureInfo.InvariantCulture, $"\n== {Snake(error.Code)} at {Stamp(error.At)} ==\n");
        report.Append(CultureInfo.InvariantCulture, $"Operation: {error.Operation}\n");
        report.Append(CultureInfo.InvariantCulture, $"Message: {error.Message}\n");
        foreach (var (key, value) in error.Context.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            report.Append(CultureInfo.InvariantCulture, $"{Capital(key)}: {Indent(value)}\n");
        if (error.Log is { Length: > 0 } log) report.Append("Log:\n  ").Append(Indent(log)).Append('\n');
        if (error.Exception is { Length: > 0 } exception) report.Append("Exception:\n  ").Append(Indent(exception)).Append('\n');
    }

    /// <summary>The project's settings without its video: the scene, and what the source is but not where.</summary>
    private async Task DescribeProjectAsync(StringBuilder report, Guid id, CancellationToken cancellationToken)
    {
        report.Append(CultureInfo.InvariantCulture, $"\n== Project {id} ==\n");
        Project project;
        try
        {
            project = (await projects.GetAsync(id, cancellationToken)).Project;
        }
        catch (AppException exception)
        {
            report.Append(CultureInfo.InvariantCulture, $"Not read: {Snake(exception.Code)}\n");
            return;
        }

        var media = project.Source.Media;
        report.Append(CultureInfo.InvariantCulture, $"Revision {project.Revision}, saved at {Stamp(project.SavedAt)}\n");
        report.Append(CultureInfo.InvariantCulture,
            $"Source: {media.Format} {media.Codec} {media.Width}x{media.Height}, {media.Fps.Num}/{media.Fps.Den} fps");
        if (media.IsVariableFrameRate) report.Append(" (variable)");
        report.Append(CultureInfo.InvariantCulture,
            $", {media.FrameCount} frames, {media.DurationSec:0.###} s, alpha {media.HasAlpha}, audio {media.HasAudio}, {media.SizeBytes} bytes\n");
        foreach (var segment in project.Scene.Segments)
        {
            report.Append(CultureInfo.InvariantCulture, $"Segment {segment.Id}: frames {segment.InFrame}-{segment.OutFrame}");
            if (segment.ExcludedFrames.Count > 0) report.Append(" without ").Append(string.Join(',', segment.ExcludedFrames));
            report.Append(CultureInfo.InvariantCulture,
                $", {Frame(segment.Frame)}, speed {segment.Speed}, loop {segment.Loop}, {segment.Preset}, long {segment.LongSticker}, autoFit {segment.AutoFit}\n");
        }
    }

    private static string Frame(SegmentFrame frame) => frame switch
    {
        SegmentFrame.Crop crop => string.Create(CultureInfo.InvariantCulture,
            $"crop {crop.Preset} {crop.Rect.X},{crop.Rect.Y} {crop.Rect.Width}x{crop.Rect.Height}"),
        SegmentFrame.Stretch stretch => string.Create(CultureInfo.InvariantCulture,
            $"stretch {stretch.Preset} {stretch.Aspect.Width}:{stretch.Aspect.Height}"),
        _ => frame.ToString(),
    };

    private static Guid? ProjectOf(ErrorRecord error) =>
        error.Context.TryGetValue(ErrorRecord.ProjectKey, out var value) && Guid.TryParse(value, out var id) ? id : null;

    /// <summary>
    /// The head whole, then as many of the newest log entries as the limit leaves room for. A head
    /// over the limit alone keeps its beginning: the versions and the error come first.
    /// </summary>
    private static string Fit(string head, IReadOnlyList<string> entries, DateTimeOffset since)
    {
        var title = $"\n== Server log since {Stamp(since)} ==\n";
        var room = LimitBytes - Encoding.UTF8.GetByteCount(head) - Encoding.UTF8.GetByteCount(title);
        if (room < 0) return Cut(head + title);

        var kept = new List<string>();
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var bytes = Encoding.UTF8.GetByteCount(entries[i]) + 1;
            if (bytes > room) break;
            room -= bytes;
            kept.Add(entries[i]);
        }
        kept.Reverse();

        var report = new StringBuilder(head).Append(title);
        var left = entries.Count - kept.Count;
        var note = string.Create(CultureInfo.InvariantCulture, $"({left} earlier entries left out)\n");
        if (left > 0 && Encoding.UTF8.GetByteCount(note) <= room) report.Append(note);
        foreach (var entry in kept) report.Append(entry).Append('\n');
        return report.ToString();
    }

    /// <summary>Keeps the beginning of a text over the limit, never splitting a character.</summary>
    private static string Cut(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= LimitBytes) return text;
        // A UTF-16 character takes up to 3 bytes: a third of the excess at a time never cuts past the limit by much
        var chars = Math.Min(text.Length, LimitBytes);
        int bytes;
        while ((bytes = Encoding.UTF8.GetByteCount(text.AsSpan(0, chars))) > LimitBytes)
            chars -= (bytes - LimitBytes + 2) / 3;
        if (chars > 0 && char.IsHighSurrogate(text[chars - 1])) chars--;
        return text[..chars];
    }

    private static string Capital(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..];

    private static string Indent(string text) => text.ReplaceLineEndings("\n  ");

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Snake<T>(T value) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
