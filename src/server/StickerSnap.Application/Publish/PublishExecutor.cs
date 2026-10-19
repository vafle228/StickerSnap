using StickerSnap.Application.Account;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Media;
using StickerSnap.Application.Packs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;

namespace StickerSnap.Application.Publish;

/// <summary>
/// «Опубликовать» (artboard 7): saved segments sent to a pack one by one, by the revision the SPA
/// saved last. A segment without a fresh export is exported first; its file is checked against
/// Telegram's rules and only a file that passes is sent (R14), the others are skipped. A new pack
/// is made by the first sticker that goes, a new version takes the place of the old sticker. When
/// Telegram asks to wait (429) the job waits and goes on by itself. Each sticker sent is in the
/// registry of packs at once, with its project; the emoji sent go to «Недавние» at the end.
/// A canceled job takes nothing back from Telegram.
/// </summary>
public sealed class PublishExecutor(
    IProjectService projects,
    IAccountService account,
    IPackService packs,
    IStickerPublisher publisher,
    ISettingsService settings,
    ISegmentExporter exporter,
    IMediaProbe probe,
    ISourcePresence presence,
    TimeProvider clock) : IJobExecutor
{
    /// <summary>How many times in a row one call to Telegram may be told to wait before the sticker fails.</summary>
    public const int MaxWaits = 5;

    /// <summary>The part of a segment's share its export takes; the send takes the rest.</summary>
    private const double ExportShare = 0.8;

    /// <summary>Failures that stop the whole publication: the next sticker would fail the same way.</summary>
    private static readonly HashSet<ErrorCode> Stopping =
    [
        ErrorCode.NetworkUnavailable,
        ErrorCode.BotBlocked,
        ErrorCode.BotUnavailable,
        ErrorCode.AccountNotLinked,
        ErrorCode.PackNotFound,
        ErrorCode.PackFull,
        ErrorCode.PackNameTaken,
        ErrorCode.DiskFull,
    ];

    public JobKind Kind => JobKind.Publish;

    /// <inheritdoc />
    /// <exception cref="AppException">
    /// account_not_linked; revision_conflict when the scene was saved since; what <see cref="PublishPlans.For"/>
    /// throws; a failure of <see cref="Stopping"/>, which leaves the segments not sent failed or skipped
    /// </exception>
    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        var request = (JobRequest.Publish)run.Request;
        await EnsureLinkedAsync(cancellationToken);
        var opened = await projects.GetAsync(request.ProjectId, cancellationToken);
        var project = opened.Project;
        if (project.Revision != request.Revision)
            throw new AppException(
                ErrorCode.RevisionConflict,
                $"Project {project.Id} is at revision {project.Revision}, the publication was asked for {request.Revision}");

        var name = request.Target.PackName;
        var known = (await packs.ListAsync(cancellationToken))
            .FirstOrDefault(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase));
        var plan = PublishPlans.For(request, [.. run.SegmentsToDo], project, known);

        var send = new Sending(run, request, plan, project, opened.SourceStatus);
        if (known is not null)
        {
            send.Pack = await CallAsync(run, ct => publisher.GetPackAsync(known.Name, ct), cancellationToken)
                ?? throw new AppException(ErrorCode.PackNotFound, $"Telegram no longer has the pack {known.Name}");
        }

        try
        {
            foreach (var step in plan.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await PublishAsync(send, step, cancellationToken);
                }
                catch (AppException exception) when (!Stopping.Contains(exception.Code))
                {
                    run.ItemFailed(step.SegmentId, exception);
                }
            }
        }
        finally
        {
            if (send.Emoji.Count > 0) await RememberAsync(send.Emoji);
        }
    }

    /// <summary>The packs are made on behalf of the linked account: without one nothing is sent.</summary>
    private async Task EnsureLinkedAsync(CancellationToken cancellationToken)
    {
        if (await account.GetAsync(cancellationToken) is not (AccountLink.Linked or AccountLink.Failed))
            throw new AppException(ErrorCode.AccountNotLinked, "No Telegram account is linked: packs are made on its behalf");
    }

    /// <summary>One segment: a checked file, then into the pack.</summary>
    private async Task PublishAsync(Sending send, PublishStep step, CancellationToken cancellationToken)
    {
        var run = send.Run;
        var segment = send.Project.Scene.Segments.First(segment => segment.Id == step.SegmentId);
        var (export, made) = await CheckedFileAsync(send, segment, cancellationToken);
        if (export.Validation.Verdict == ValidationVerdict.Fail)
        {
            run.ItemSkipped(segment.Id, new AppException(
                ErrorCode.ValidationFailed, $"{export.FileName} breaks Telegram's rules: it is not sent"));
            return;
        }

        run.Stage(JobStage.Send);
        run.Item(segment.Id, JobItemState.Sending, ExportShare);
        var sticker = await SendAsync(send, step, segment, export.Path, cancellationToken);
        send.Emoji.AddRange(step.Emoji);
        run.Done(segment.Id, export: made ? export : null, sticker: sticker);
    }

    /// <summary>
    /// The segment's file, checked: its last export when it is fresh and still on disk, checked
    /// again; otherwise a new export, which comes checked.
    /// </summary>
    /// <returns>The export, and whether this job made it</returns>
    private async Task<(SegmentExport Export, bool Made)> CheckedFileAsync(
        Sending send, Segment segment, CancellationToken cancellationToken)
    {
        var run = send.Run;
        var project = send.Project;
        if (project.Exports.GetValueOrDefault(segment.Id) is { } last &&
            !last.IsStaleIn(project) &&
            presence.Of(last.Path) == PresenceStatus.Ok)
        {
            run.Stage(JobStage.Validate);
            run.Item(segment.Id, JobItemState.Validating, ExportShare / 2);
            try
            {
                var media = await probe.ProbeAsync(last.Path, cancellationToken);
                var validation = StickerValidator.Validate(media, segment.Preset, TelegramLimits.Current.For(segment.Preset));
                return (last with { Bytes = media.SizeBytes, Validation = validation }, false);
            }
            catch (AppException exception) when (exception.Code == ErrorCode.SourceNotFound)
            {
                // Gone between the look and the probe: made again below
            }
        }

        if (send.SourceStatus == PresenceStatus.Missing)
            throw new AppException(ErrorCode.SourceNotFound, $"The video of project {project.Id} is not at {project.Source.Path}");

        run.Item(segment.Id, JobItemState.Exporting, 0);
        send.StickersFolder ??= (await settings.GetAsync(cancellationToken)).Folders.Stickers;
        var export = await exporter.ExportAsync(
            project, segment, send.StickersFolder, new SegmentJobProgress(run, segment.Id, ExportShare), cancellationToken);
        return (export, true);
    }

    /// <summary>Uploads the file once, puts it into the pack and writes the sticker into the registry.</summary>
    private async Task<StickerRef> SendAsync(
        Sending send, PublishStep step, Segment segment, string path, CancellationToken cancellationToken)
    {
        var run = send.Run;
        var name = send.Request.Target.PackName;
        var file = await CallAsync(run, ct => publisher.UploadAsync(path, ct), cancellationToken);
        var sticker = new NewSticker(file, step.Emoji);

        var before = send.Pack;
        int? position = null;
        PublishedPack after;
        if (before is null)
        {
            var title = send.Request.Target is PublishTarget.New target ? target.Title : name;
            after = await CallAsync(run, ct => publisher.CreatePackAsync(name, title, send.Plan.Kind, [sticker], ct), cancellationToken);
        }
        else if (step.Move == PublishMove.Replace &&
                 before.Stickers.ToList().FindIndex(old => old.UniqueId == step.OldStickerId) is var at and >= 0)
        {
            position = at;
            after = await CallAsync(run, ct => publisher.ReplaceStickerAsync(before.Name, before.Stickers[at].Id, sticker, ct), cancellationToken);
        }
        else
        {
            // A sticker to replace that was deleted meanwhile: the new version goes to the end
            after = await CallAsync(run, ct => publisher.AddStickerAsync(before.Name, sticker, ct), cancellationToken);
        }

        send.Pack = after;
        var sent = after.SentSince(before, position)
            ?? throw new AppException(ErrorCode.TelegramError, $"{name} has no stickers after a send") { Log = "getStickerSet gave an empty pack" };

        var origin = new StickerOrigin(send.Project.Id, send.Project.Name, segment.Id);
        await packs.RecordSentAsync(after, sent.UniqueId, step.Emoji, origin, CancellationToken.None);
        return new StickerRef(after.Name, sent.UniqueId);
    }

    /// <summary>
    /// A call to Telegram; when it asks to wait (429), the job shows the wait, then calls again,
    /// at most <see cref="MaxWaits"/> times in a row.
    /// </summary>
    private async Task<T> CallAsync<T>(JobRun run, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        for (var waits = 0; ; waits++)
        {
            try
            {
                return await call(cancellationToken);
            }
            catch (AppException exception) when (exception.Code == ErrorCode.RateLimited && waits < MaxWaits)
            {
                var seconds = exception.Params.TryGetValue("retryAfterSec", out var after) ? Convert.ToDouble(after) : 1;
                await run.WaitAsync(clock.GetUtcNow().AddSeconds(Math.Max(seconds, 1)), cancellationToken);
                run.Stage(JobStage.Send);
            }
        }
    }

    /// <summary>«Недавние» get the emoji sent, even when the job was canceled or stopped after some went.</summary>
    private async Task RememberAsync(IReadOnlyList<string> emoji)
    {
        try
        {
            await settings.RememberEmojiAsync(emoji, CancellationToken.None);
        }
        catch (AppException)
        {
            // The stickers are in the pack; only the picker's «Недавние» missed them
        }
    }

    /// <summary>What one run of the publication carries from sticker to sticker.</summary>
    private sealed class Sending(
        JobRun run, JobRequest.Publish request, PublishPlan plan, Project project, PresenceStatus sourceStatus)
    {
        public JobRun Run { get; } = run;

        public JobRequest.Publish Request { get; } = request;

        public PublishPlan Plan { get; } = plan;

        public Project Project { get; } = project;

        public PresenceStatus SourceStatus { get; } = sourceStatus;

        /// <summary>The pack as Telegram had it after the last send; null while a new pack is not made yet.</summary>
        public PublishedPack? Pack { get; set; }

        public string? StickersFolder { get; set; }

        /// <summary>The emoji of the stickers sent, in their order.</summary>
        public List<string> Emoji { get; } = [];
    }
}
