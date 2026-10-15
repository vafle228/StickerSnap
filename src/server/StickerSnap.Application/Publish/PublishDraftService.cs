using StickerSnap.Application.Account;
using StickerSnap.Application.Packs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Publish;

namespace StickerSnap.Application.Publish;

/// <summary>
/// The publish window's draft, read from what the app has: the saved project and its exports, the
/// registry of packs and the settings. Telegram is not asked: the packs are as the last check left them.
/// </summary>
public sealed class PublishDraftService(
    IProjectService projects,
    IPackService packs,
    IAccountService account,
    ISettingsService settings) : IPublishDraftService
{
    /// <inheritdoc />
    public async Task<PublishDraft> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = (await projects.GetAsync(projectId, cancellationToken)).Project;
        var link = await account.GetAsync(cancellationToken);
        var known = await packs.ListAsync(cancellationToken);
        var recent = (await settings.GetAsync(cancellationToken)).RecentEmoji;

        var segments = project.Scene.Segments.Select(segment => new DraftSegment(
            segment.Id,
            segment.Name,
            segment.PlaySeconds(project.Source),
            segment.Preset,
            project.Exports.GetValueOrDefault(segment.Id) is { } export
                ? new DraftExport(export.Bytes, export.Validation.Verdict, export.IsStaleIn(project))
                : null,
            [.. known.SelectMany(pack => pack.Stickers
                .Where(sticker => sticker.Origin?.ProjectId == project.Id && sticker.Origin.SegmentId == segment.Id)
                .Select(sticker => new StickerRef(pack.Name, sticker.Id)))])).ToList();

        // The registry keeps packs in the order they came: the last one with this project's stickers
        var lastPack = known.LastOrDefault(pack => pack.Stickers.Any(sticker => sticker.Origin?.ProjectId == project.Id))?.Name;
        var kind = PublishPlan.KindFor(project.Scene.Segments.Select(segment => segment.Preset));
        return new PublishDraft(
            link,
            segments,
            lastPack,
            [.. known.Select(pack => new DraftPack(pack, PublishPlan.RefusalOf(pack, kind)))],
            recent);
    }
}
