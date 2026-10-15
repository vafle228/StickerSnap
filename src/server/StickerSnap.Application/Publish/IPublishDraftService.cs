using StickerSnap.Core.Account;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;
using StickerSnap.Core.Validation;

namespace StickerSnap.Application.Publish;

/// <summary>GET /api/v1/projects/{id}/publish-draft: everything the publish window asks about one project (artboard 7).</summary>
public interface IPublishDraftService
{
    /// <exception cref="Core.Errors.AppException">project_not_found, project_invalid, project_too_new</exception>
    Task<PublishDraft> GetAsync(Guid projectId, CancellationToken cancellationToken);
}

/// <summary>What the publish window opens with (contract, PublishDraft).</summary>
/// <param name="Segments">The saved segments, in the scene's order</param>
/// <param name="LastPack">The pack the project's stickers went to last; null when none was sent</param>
/// <param name="Packs">Every pack of the registry, with why the window cannot pick it</param>
/// <param name="RecentEmoji">«Недавние» of the emoji picker</param>
public sealed record PublishDraft(
    AccountLink Account,
    IReadOnlyList<DraftSegment> Segments,
    string? LastPack,
    IReadOnlyList<DraftPack> Packs,
    IReadOnlyList<string> RecentEmoji);

/// <param name="DurationSec">How long the sticker plays</param>
/// <param name="Export">Its last export; null when it was never exported or the file is gone</param>
/// <param name="PublishedIn">Where it was sent before: «Заменит старую версию»</param>
public sealed record DraftSegment(
    string SegmentId,
    string Name,
    double DurationSec,
    SegmentPreset Preset,
    DraftExport? Export,
    IReadOnlyList<StickerRef> PublishedIn);

/// <param name="Stale">The segment was edited after it: the publication exports it again</param>
/// <param name="FileId">The .webm, which the window plays as the row's preview</param>
public sealed record DraftExport(long Bytes, ValidationVerdict Verdict, bool Stale, string FileId);

/// <param name="Refusal">Null when the window can pick it</param>
public sealed record DraftPack(Pack Pack, PackRefusal? Refusal)
{
    public bool Selectable => Refusal is null;
}
