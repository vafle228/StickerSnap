using System.Text.Json.Serialization;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using Domain = StickerSnap.Core.Packs;

namespace StickerSnap.Api.Contracts;

/// <summary>A row of «Мои паки»: GET /api/v1/packs, event packChanged (schema PackSummary).</summary>
/// <param name="Type">PackType: regular or custom_emoji</param>
/// <param name="Limit">120 or 200</param>
/// <param name="CoverUrl">The first sticker's file (G5); none for an empty pack</param>
/// <param name="CheckedAt">«Сверено с Telegram 2 минуты назад»</param>
/// <param name="Sending">«Отправляем… 2 из 4»: the publish job of B24</param>
public sealed record PackSummary(
    string Name,
    string Title,
    PackKind Type,
    int Count,
    int Limit,
    string? CoverUrl,
    PresenceStatus Status,
    DateTimeOffset? CheckedAt,
    PackSending? Sending)
{
    public static PackSummary From(Domain.Pack pack) => new(
        pack.Name,
        pack.Title,
        pack.Kind,
        pack.Count,
        pack.Limit,
        pack.Stickers.Count > 0 ? PackUrls.Media(pack.Name, pack.Stickers[0].Id) : null,
        pack.Status,
        pack.CheckedAt,
        null);
}

public sealed record PackSending(string JobId, int Done, int Total);

/// <summary>A pack with its stickers: GET /api/v1/packs/{pack}, sync, import (schema Pack, PackSummary with these).</summary>
/// <param name="Link">https://t.me/addstickers/&lt;name&gt;</param>
public sealed record Pack(
    string Name,
    string Title,
    PackKind Type,
    int Count,
    int Limit,
    string? CoverUrl,
    PresenceStatus Status,
    DateTimeOffset? CheckedAt,
    PackSending? Sending,
    string Link,
    IReadOnlyList<PackSticker> Stickers)
{
    public static Pack From(PackView view)
    {
        var summary = PackSummary.From(view.Pack);
        return new Pack(
            summary.Name,
            summary.Title,
            summary.Type,
            summary.Count,
            summary.Limit,
            summary.CoverUrl,
            summary.Status,
            summary.CheckedAt,
            summary.Sending,
            PackUrls.Link(view.Pack.Name),
            [.. view.Stickers.Select(sticker => PackSticker.From(view.Pack.Name, sticker))]);
    }
}

/// <param name="Id">file_unique_id from Telegram</param>
/// <param name="Origin">None for a sticker not sent from here</param>
public sealed record PackSticker(string Id, IReadOnlyList<string> Emoji, string MediaUrl, StickerOrigin? Origin)
{
    public static PackSticker From(string pack, PackStickerView view) => new(
        view.Sticker.Id,
        view.Sticker.Emoji,
        PackUrls.Media(pack, view.Sticker.Id),
        view.Origin is { } origin ? new StickerOrigin(origin.ProjectId, origin.ProjectName, origin.SegmentId, origin.ProjectStatus) : null);
}

public sealed record StickerOrigin(Guid ProjectId, string ProjectName, string SegmentId, PresenceStatus ProjectStatus);

/// <summary>POST /api/v1/packs/import (schema ImportPackRequest).</summary>
/// <param name="Link">https://t.me/addstickers/&lt;name&gt;</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportPackRequest(string Link);

/// <summary>POST /api/v1/packs/name-check (schema PackNameCheckRequest).</summary>
/// <param name="Name">What the user typed over the suggested link; the title's link without it</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PackNameCheckRequest(string Title, string? Name);

/// <summary>The answer to POST /api/v1/packs/name-check (schema PackNameCheck).</summary>
/// <param name="Name">The part before _by_StickerSnapAppBot, at most 43</param>
/// <param name="Suggestion">«Взять kot_divan_2»</param>
public sealed record PackNameCheck(string Name, string FullName, IReadOnlyList<PackNameProblem> Problems, string? Suggestion)
{
    public static PackNameCheck From(Application.Packs.PackNameCheck check) =>
        new(check.Name, check.FullName, check.Problems, check.Suggestion);
}

public static class PackUrls
{
    public static string Link(string pack) => $"https://t.me/addstickers/{pack}";

    /// <summary>Through the server, which keeps the bot's token; a file_unique_id never changes its file, so no `v`.</summary>
    public static string Media(string pack, string stickerId) =>
        $"/api/v1/packs/{Uri.EscapeDataString(pack)}/stickers/{Uri.EscapeDataString(stickerId)}/media";
}
