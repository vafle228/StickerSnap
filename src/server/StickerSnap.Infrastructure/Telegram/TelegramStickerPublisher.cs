using System.Net.Http.Headers;
using System.Text.Json;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;

namespace StickerSnap.Infrastructure.Telegram;

/// <summary>The packs of @StickerSnapAppBot in Telegram, through the Bot API's sticker methods.</summary>
internal sealed class TelegramStickerPublisher(BotApiClient api) : IStickerPublisher
{
    /// <summary>What createNewStickerSet takes at once.</summary>
    public const int MaxStickersAtCreation = 50;

    private const string VideoFormat = "video";

    public async Task<PublishedPack?> GetPackAsync(string name, CancellationToken cancellationToken)
    {
        BotStickerSet set;
        try
        {
            set = await api.CallAsync<BotStickerSet>("getStickerSet", new { Name = name }, cancellationToken);
        }
        catch (AppException exception) when (exception.Code == ErrorCode.PackNotFound)
        {
            return null;
        }

        return new PublishedPack(
            set.Name,
            set.Title,
            KindOf(set),
            [.. set.Stickers.Select(sticker => new PublishedSticker(sticker.FileId, sticker.FileUniqueId, sticker.Emoji))]);
    }

    public async Task<StickerFile.Uploaded> UploadAsync(long ownerId, string path, CancellationToken cancellationToken)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(ownerId.ToString()), "user_id" },
            { new StringContent(VideoFormat), "sticker_format" },
        };
        AttachFile(form, "sticker", path);

        var file = await api.CallAsync<BotFile>("uploadStickerFile", form, cancellationToken);
        return new StickerFile.Uploaded(file.FileId);
    }

    public Task CreatePackAsync(
        long ownerId,
        string name,
        string title,
        PackKind kind,
        IReadOnlyList<NewSticker> stickers,
        CancellationToken cancellationToken)
    {
        if (stickers.Count is 0 or > MaxStickersAtCreation)
            throw new ArgumentOutOfRangeException(nameof(stickers), stickers.Count, $"A pack is made with 1 to {MaxStickersAtCreation} stickers");

        var form = new MultipartFormDataContent
        {
            { new StringContent(ownerId.ToString()), "user_id" },
            { new StringContent(name), "name" },
            { new StringContent(title), "title" },
            { new StringContent(kind == PackKind.CustomEmoji ? "custom_emoji" : "regular"), "sticker_type" },
        };
        var inputs = stickers.Select((sticker, index) => Input(form, sticker, $"sticker{index}")).ToList();
        form.Add(new StringContent(JsonSerializer.Serialize(inputs, BotApiClient.Json)), "stickers");

        return api.CallAsync<bool>("createNewStickerSet", form, cancellationToken);
    }

    public Task AddStickerAsync(long ownerId, string pack, NewSticker sticker, CancellationToken cancellationToken)
    {
        var form = StickerForm(ownerId, pack, sticker);
        return api.CallAsync<bool>("addStickerToSet", form, cancellationToken);
    }

    public Task ReplaceStickerAsync(
        long ownerId,
        string pack,
        string oldStickerId,
        NewSticker sticker,
        CancellationToken cancellationToken)
    {
        var form = StickerForm(ownerId, pack, sticker);
        form.Add(new StringContent(oldStickerId), "old_sticker");
        return api.CallAsync<bool>("replaceStickerInSet", form, cancellationToken);
    }

    public Task DeleteStickerAsync(string stickerId, CancellationToken cancellationToken) =>
        api.CallAsync<bool>("deleteStickerFromSet", new { Sticker = stickerId }, cancellationToken);

    public async Task DownloadAsync(string stickerId, Stream destination, CancellationToken cancellationToken)
    {
        var file = await api.CallAsync<BotFile>("getFile", new { FileId = stickerId }, cancellationToken);
        if (file.FilePath is not { Length: > 0 } path)
            throw new AppException(ErrorCode.TelegramError, $"getFile gave no path for {stickerId}") { Log = "getFile gave no file_path" };

        await api.DownloadAsync(path, destination, cancellationToken);
    }

    private static MultipartFormDataContent StickerForm(long ownerId, string pack, NewSticker sticker)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(ownerId.ToString()), "user_id" },
            { new StringContent(pack), "name" },
        };
        form.Add(new StringContent(JsonSerializer.Serialize(Input(form, sticker, "sticker0"), BotApiClient.Json)), "sticker");
        return form;
    }

    /// <summary>The InputSticker of <paramref name="sticker"/>; a file on disk goes into the form as <paramref name="part"/>.</summary>
    private static BotInputSticker Input(MultipartFormDataContent form, NewSticker sticker, string part)
    {
        switch (sticker.File)
        {
            case StickerFile.Uploaded uploaded:
                return new BotInputSticker(uploaded.Id, VideoFormat, sticker.Emoji);
            case StickerFile.Local local:
                AttachFile(form, part, local.Path);
                return new BotInputSticker($"attach://{part}", VideoFormat, sticker.Emoji);
            default:
                throw new ArgumentOutOfRangeException(nameof(sticker), sticker.File, "An unknown kind of sticker file");
        }
    }

    private static void AttachFile(MultipartFormDataContent form, string part, string path)
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            form.Dispose();
            throw new AppException(ErrorCode.FileNotFound, $"The sticker {path} is gone", exception);
        }

        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/webm");
        form.Add(content, part, $"{part}.webm");
    }

    /// <exception cref="AppException">not_our_pack for a pack of masks: StickerSnap never makes one</exception>
    private static PackKind KindOf(BotStickerSet set) => set.StickerType switch
    {
        "regular" => PackKind.Regular,
        "custom_emoji" => PackKind.CustomEmoji,
        _ => throw new AppException(ErrorCode.NotOurPack, $"{set.Name} is a pack of {set.StickerType}"),
    };
}
