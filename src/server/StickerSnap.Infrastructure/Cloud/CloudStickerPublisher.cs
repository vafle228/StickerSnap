using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// The packs of @StickerSnapAppBot through the cloud service: it calls the Bot API on behalf of
/// the account this computer's key is linked to, so the desktop holds no bot token. The service
/// writes only to packs in its registry, so another person's pack reads as none.
/// </summary>
internal sealed class CloudStickerPublisher(CloudClient client) : IStickerPublisher
{
    public async Task<PublishedPack?> GetPackAsync(string name, CancellationToken cancellationToken) =>
        (await client.GetPackAsync(name, cancellationToken))?.ToPublishedPack();

    public Task<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken) =>
        client.IsPackNameTakenAsync(name, cancellationToken);

    public async Task<UploadedFile> UploadAsync(string path, CancellationToken cancellationToken)
    {
        FileStream webm;
        try
        {
            webm = File.OpenRead(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new AppException(ErrorCode.FileNotFound, $"The sticker {path} is gone", exception);
        }

        await using (webm)
        {
            var uploaded = await client.UploadStickerAsync(webm, cancellationToken);
            return new UploadedFile(uploaded.Id);
        }
    }

    public async Task<PublishedPack> CreatePackAsync(
        string name,
        string title,
        PackKind kind,
        IReadOnlyList<NewSticker> stickers,
        CancellationToken cancellationToken)
    {
        var body = new NewPackBody(
            name,
            title,
            kind == PackKind.CustomEmoji ? CloudPackKind.Emoji : CloudPackKind.Stickers,
            [.. stickers.Select(Input)]);
        return (await client.CreatePackAsync(body, cancellationToken)).ToPublishedPack();
    }

    public async Task<PublishedPack> AddStickerAsync(string pack, NewSticker sticker, CancellationToken cancellationToken) =>
        (await client.AddStickerAsync(pack, Input(sticker), cancellationToken)).ToPublishedPack();

    public async Task<PublishedPack> ReplaceStickerAsync(
        string pack,
        string oldStickerId,
        NewSticker sticker,
        CancellationToken cancellationToken) =>
        (await client.ReplaceStickerAsync(pack, oldStickerId, Input(sticker), cancellationToken)).ToPublishedPack();

    public Task DeleteStickerAsync(string pack, string stickerId, CancellationToken cancellationToken) =>
        client.RemoveStickerAsync(pack, stickerId, cancellationToken);

    public async Task DownloadAsync(string stickerId, string path, CancellationToken cancellationToken)
    {
        await using var destination = File.Create(path);
        await client.DownloadStickerAsync(stickerId, destination, cancellationToken);
    }

    private static InputSticker Input(NewSticker sticker) => new(sticker.File.Id, sticker.Emoji);
}
