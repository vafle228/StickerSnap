using StickerSnap.Core.Account;
using StickerSnap.Core.Packs;

namespace StickerSnap.Infrastructure.Cloud;

// The bodies of src/contract/bot-service.yaml, as the service reads and writes them: camelCase,
// enums as lowercase strings, an absent field rather than null. Kept apart from the domain's
// types so a rename in the code does not change what goes over the wire.

internal enum DeviceOs
{
    Windows,
    Macos,
    Linux,
}

internal enum FeedbackType
{
    Bug,
    Idea,
    Other,
}

internal enum CloudPackKind
{
    Stickers,
    Emoji,
}

internal enum LinkStatus
{
    Pending,
    Linked,
    Declined,
    Expired,
}

internal sealed record LinkRequestBody(string Name, DeviceOs Os, Guid InstallationId, string? Platform, string? AppVersion);

internal sealed record LinkCreated(string Code, string DeepLink, string PollToken, DateTimeOffset ExpiresAt);

internal sealed record LinkState(
    LinkStatus Status,
    DateTimeOffset? ExpiresAt,
    string? DeviceKey,
    CloudDevice? Device,
    CloudUser? User);

internal sealed record LinkedDeviceBody(CloudDevice Device, CloudUser User);

internal sealed record CloudDevice(string Id, string Name, DeviceOs Os, DateTimeOffset LinkedAt);

internal sealed record CloudUser(long Id, string FirstName, string? LastName, string? Username, bool BotBlocked)
{
    public LinkedUser ToLinkedUser() => new(Id, FirstName, Username);
}

internal sealed record FeedbackRequest(
    FeedbackType Type,
    string Text,
    bool CanReply,
    string? Contact,
    Guid? InstallationId,
    string? AppVersion,
    string? Platform,
    FeedbackErrorBody? Error,
    string? Diagnostics);

internal sealed record FeedbackErrorBody(string Code, string? TraceId);

internal sealed record FeedbackCreated(long Id);

internal sealed record UploadedSticker(string Id);

internal sealed record InputSticker(string File, IReadOnlyList<string> Emoji);

internal sealed record NewPackBody(string Name, string Title, CloudPackKind Kind, IReadOnlyList<InputSticker> Stickers);

internal sealed record CopyPackBody(string Source, string Name, string Title);

internal sealed record CloudPack(string Name, string Title, CloudPackKind Kind, IReadOnlyList<CloudSticker> Stickers)
{
    public PublishedPack ToPublishedPack() => new(
        Name,
        Title,
        Kind == CloudPackKind.Emoji ? PackKind.CustomEmoji : PackKind.Regular,
        [.. Stickers.Select(sticker => new PublishedSticker(
            sticker.Id, sticker.UniqueId, sticker.Emoji.FirstOrDefault(), sticker.Format ?? StickerFormat.Video))]);
}

internal sealed record PackNameState(string Name, bool Taken);

/// <param name="Format">Null from a service older than pack copies: a video</param>
internal sealed record CloudSticker(string Id, string UniqueId, IReadOnlyList<string> Emoji, StickerFormat? Format = null);

/// <summary>RFC 9457 Problem Details with the service's code; the words are English, for the log.</summary>
internal sealed record CloudProblem(string? Code, string? Title, string? Detail, Dictionary<string, System.Text.Json.JsonElement>? Params);
