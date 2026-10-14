using System.Text.Json;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;
using StickerSnap.Infrastructure.Telegram;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>The sticker methods of the Bot API, against answers taken from it.</summary>
public sealed class TelegramStickerPublisherTests : IDisposable
{
    private readonly BotApiStub _api = new();
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-publish-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private TelegramStickerPublisher Publisher => new(_api.Client());

    public void Dispose()
    {
        _api.Dispose();
        _files.Delete(recursive: true);
    }

    private string Webm(string name = "sticker.webm")
    {
        var path = Path.Combine(_files.FullName, name);
        File.WriteAllBytes(path, [0x1A, 0x45, 0xDF, 0xA3]);
        return path;
    }

    [Fact]
    public async Task Reads_a_pack_with_its_stickers()
    {
        var pack = await Publisher.GetPackAsync("cats_by_StickerSnapAppBot", Cancel);

        pack.Should().BeEquivalentTo(new PublishedPack(
            "cats_by_StickerSnapAppBot",
            "Коты",
            PackKind.Regular,
            [new PublishedSticker("CAACAgIAAxkBAAIBc2", "AgADc2", "😂"), new PublishedSticker("CAACAgIAAxkBAAIBc3", "AgADc3", null)]));
        _api.Single("getStickerSet").Body.Should().Be("{\"name\":\"cats_by_StickerSnapAppBot\"}");
    }

    [Fact]
    public async Task Gives_no_pack_when_telegram_has_none_of_that_name()
    {
        _api.Refuse("getStickerSet", 400, "Bad Request: STICKERSET_INVALID");

        var pack = await Publisher.GetPackAsync("gone_by_StickerSnapAppBot", Cancel);

        pack.Should().BeNull();
    }

    [Fact]
    public async Task Calls_a_pack_of_masks_not_ours()
    {
        _api.Answer("getStickerSet", """{"ok":true,"result":{"name":"m","title":"M","sticker_type":"mask","stickers":[]}}""");

        var reading = () => Publisher.GetPackAsync("m", Cancel);

        (await reading.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NotOurPack);
    }

    [Fact]
    public async Task Uploads_a_webm_as_a_video_sticker_file()
    {
        var uploaded = await Publisher.UploadAsync(42, Webm(), Cancel);

        uploaded.Should().Be(new StickerFile.Uploaded("CAACAgIAAxkBUPLOADED"));
        var request = _api.Single("uploadStickerFile");
        request.ContentType.Should().Be("multipart/form-data");
        request.Body.Should().ContainAll("name=user_id", "42", "name=sticker_format", "video", "name=sticker; filename=sticker.webm", "video/webm");
    }

    [Fact]
    public async Task Creates_a_pack_with_files_attached_and_ones_sent_before()
    {
        NewSticker[] stickers =
        [
            new(new StickerFile.Local(Webm()), ["😂"]),
            new(new StickerFile.Uploaded("CAACAgIAAxkBUPLOADED"), ["🔥", "🐱"]),
        ];

        await Publisher.CreatePackAsync(42, "cats_by_StickerSnapAppBot", "Коты", PackKind.CustomEmoji, stickers, Cancel);

        var body = _api.Single("createNewStickerSet").Body;
        body.Should().ContainAll("name=sticker_type", "custom_emoji", "name=title", "Коты", "name=sticker0; filename=sticker0.webm");
        StickersOf(body).Should().BeEquivalentTo(
            [
                new BotInputSticker("attach://sticker0", "video", ["😂"]),
                new BotInputSticker("CAACAgIAAxkBUPLOADED", "video", ["🔥", "🐱"]),
            ]);
    }

    [Fact]
    public async Task Makes_no_pack_without_stickers_or_with_more_than_telegram_takes()
    {
        var empty = () => Publisher.CreatePackAsync(42, "a_by_StickerSnapAppBot", "A", PackKind.Regular, [], Cancel);
        var many = () => Publisher.CreatePackAsync(
            42, "a_by_StickerSnapAppBot", "A", PackKind.Regular,
            [.. Enumerable.Repeat(new NewSticker(new StickerFile.Uploaded("x"), ["😂"]), 51)], Cancel);

        await empty.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await many.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Says_the_name_is_taken_when_telegram_has_such_a_pack()
    {
        _api.Refuse("createNewStickerSet", 400, "Bad Request: sticker set name is already occupied");

        var creating = () => Publisher.CreatePackAsync(
            42, "cats_by_StickerSnapAppBot", "Коты", PackKind.Regular, [new(new StickerFile.Uploaded("x"), ["😂"])], Cancel);

        (await creating.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNameTaken);
    }

    [Fact]
    public async Task Adds_a_sticker_to_a_pack()
    {
        await Publisher.AddStickerAsync(42, "cats_by_StickerSnapAppBot", new NewSticker(new StickerFile.Local(Webm()), ["😂"]), Cancel);

        var body = _api.Single("addStickerToSet").Body;
        body.Should().ContainAll("name=user_id", "name=name", "cats_by_StickerSnapAppBot", "name=sticker0; filename=sticker0.webm");
        StickerOf(body).Sticker.Should().Be("attach://sticker0");
    }

    [Fact]
    public async Task Says_the_pack_is_full()
    {
        _api.Refuse("addStickerToSet", 400, "Bad Request: STICKERS_TOO_MUCH");

        var adding = () => Publisher.AddStickerAsync(42, "cats_by_StickerSnapAppBot", new NewSticker(new StickerFile.Uploaded("x"), ["😂"]), Cancel);

        (await adding.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackFull);
    }

    [Fact]
    public async Task Replaces_a_sticker_in_its_place()
    {
        await Publisher.ReplaceStickerAsync(
            42, "cats_by_StickerSnapAppBot", "CAACAgIAAxkBAAIBc2", new NewSticker(new StickerFile.Uploaded("CAACAgIAAxkBUPLOADED"), ["😂"]), Cancel);

        var body = _api.Single("replaceStickerInSet").Body;
        body.Should().ContainAll("name=old_sticker", "CAACAgIAAxkBAAIBc2");
        StickerOf(body).Sticker.Should().Be("CAACAgIAAxkBUPLOADED");
    }

    [Fact]
    public async Task Deletes_a_sticker_by_its_id()
    {
        await Publisher.DeleteStickerAsync("CAACAgIAAxkBAAIBc2", Cancel);

        _api.Single("deleteStickerFromSet").Body.Should().Be("{\"sticker\":\"CAACAgIAAxkBAAIBc2\"}");
    }

    [Fact]
    public async Task Downloads_a_sticker_from_where_get_file_points()
    {
        _api.Answer("file", "webm bytes");
        var destination = Path.Combine(_files.FullName, "downloaded.webm");

        await Publisher.DownloadAsync("CAACAgIAAxkBAAIBc2", destination, Cancel);

        (await File.ReadAllTextAsync(destination, Cancel)).Should().Be("webm bytes");
        _api.Single("file").Address.AbsolutePath.Should().Be($"/file/bot{BotApiStub.Token}/stickers/file_7.webm");
    }

    [Fact]
    public async Task Says_a_sticker_file_that_is_gone_is_not_found_and_sends_nothing()
    {
        var uploading = () => Publisher.UploadAsync(42, Path.Combine(_files.FullName, "gone.webm"), Cancel);

        (await uploading.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.FileNotFound);
        _api.Requests.Should().BeEmpty();
    }

    private static BotInputSticker[] StickersOf(string multipart) =>
        JsonSerializer.Deserialize<BotInputSticker[]>(PartOf(multipart, "stickers"), BotApiClient.Json)!;

    private static BotInputSticker StickerOf(string multipart) =>
        JsonSerializer.Deserialize<BotInputSticker>(PartOf(multipart, "sticker"), BotApiClient.Json)!;

    /// <summary>The text of a form field: what follows its headers up to the next boundary.</summary>
    private static string PartOf(string multipart, string name)
    {
        var start = multipart.IndexOf($"name={name}\r\n", StringComparison.Ordinal);
        start.Should().BePositive($"the form has a field {name}");
        var value = multipart.IndexOf("\r\n\r\n", start, StringComparison.Ordinal) + 4;
        return multipart[value..multipart.IndexOf("\r\n--", value, StringComparison.Ordinal)];
    }
}
