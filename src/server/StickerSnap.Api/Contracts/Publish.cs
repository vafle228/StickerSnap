using StickerSnap.Application.Publish;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;
using StickerSnap.Core.Validation;

namespace StickerSnap.Api.Contracts;

/// <summary>GET /api/v1/projects/{id}/publish-draft (schema PublishDraft): everything the publish window asks.</summary>
/// <param name="LastPack">The pack of the project's last publication</param>
public sealed record PublishDraft(
    AccountLink Account,
    IReadOnlyList<PublishDraftSegment> Segments,
    string? LastPack,
    IReadOnlyList<PublishDraftPack> Packs,
    IReadOnlyList<string> RecentEmoji)
{
    public static PublishDraft From(Application.Publish.PublishDraft draft, IReadOnlyList<Core.Jobs.Job> jobs) => new(
        AccountLink.From(draft.Account),
        [.. draft.Segments.Select(PublishDraftSegment.From)],
        draft.LastPack,
        [.. draft.Packs.Select(pack => PublishDraftPack.From(pack, jobs))],
        draft.RecentEmoji);
}

/// <param name="PublishedIn">For «Заменит старую версию»</param>
public sealed record PublishDraftSegment(
    string SegmentId,
    string Name,
    double DurationSec,
    SegmentPreset Preset,
    PublishDraftExport? Export,
    IReadOnlyList<StickerRef> PublishedIn)
{
    public static PublishDraftSegment From(DraftSegment segment) => new(
        segment.SegmentId,
        segment.Name,
        segment.DurationSec,
        segment.Preset,
        segment.Export is { } export ? new PublishDraftExport(export.Bytes, export.Verdict, export.Stale) : null,
        [.. segment.PublishedIn.Select(StickerRef.From)]);
}

public sealed record PublishDraftExport(long Bytes, ValidationVerdict Verdict, bool Stale);

/// <summary>A pack of the registry (PackSummary) and whether the window can pick it.</summary>
public sealed record PublishDraftPack(
    string Name,
    string Title,
    PackKind Type,
    int Count,
    int Limit,
    string? CoverUrl,
    PresenceStatus Status,
    DateTimeOffset? CheckedAt,
    PackSending? Sending,
    bool Selectable,
    PackRefusal? Reason)
{
    public static PublishDraftPack From(DraftPack pack, IReadOnlyList<Core.Jobs.Job> jobs)
    {
        var summary = PackSummary.From(pack.Pack, jobs);
        return new PublishDraftPack(
            summary.Name,
            summary.Title,
            summary.Type,
            summary.Count,
            summary.Limit,
            summary.CoverUrl,
            summary.Status,
            summary.CheckedAt,
            summary.Sending,
            pack.Selectable,
            pack.Refusal);
    }
}
