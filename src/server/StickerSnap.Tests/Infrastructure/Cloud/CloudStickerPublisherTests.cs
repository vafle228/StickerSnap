using System.Net;
using System.Text.Json.Nodes;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>The packs of the bot through the service: the port's calls as the service's requests, with this computer's key.</summary>
public sealed class CloudStickerPublisherTests : IDisposable, IAsyncLifetime
{
    private const string Cats = "cats_by_StickerSnapAppBot";

    private readonly CloudServiceStub _service = new();
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-publisher-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string PackJson => TestFiles.ReadText("Cloud/cats.json");

    private CloudStickerPublisher Publisher => new(_service.Client());

    public async ValueTask InitializeAsync() => await _service.LinkAsync(Cancel);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        _service.Dispose();
        _files.Delete(recursive: true);
    }

    [Fact]
    public async Task Uploads_the_file_as_it_is_and_gives_its_id()
    {
        var path = Path.Combine(_files.FullName, "s1.webm");
        await File.WriteAllBytesAsync(path, [0x1A, 0x45, 0xDF, 0xA3, 7], Cancel);
        _service.Answer("POST /v1/stickers/upload", """{"id":"file-1"}""", HttpStatusCode.Created);

        var uploaded = await Publisher.UploadAsync(path, Cancel);

        uploaded.Should().Be(new UploadedFile("file-1"));
        var request = _service.Single("POST /v1/stickers/upload");
        request.ContentType.Should().Be("video/webm");
        request.Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
    }

    [Fact]
    public async Task Says_a_sticker_file_gone_from_disk_is_not_found_without_a_request()
    {
        var uploading = () => Publisher.UploadAsync(Path.Combine(_files.FullName, "gone.webm"), Cancel);

        (await uploading.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.FileNotFound);
        _service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Creates_a_pack_of_emoji_and_answers_the_pack_after_it()
    {
        _service.Answer("POST /v1/packs", PackJson, HttpStatusCode.Created);

        var pack = await Publisher.CreatePackAsync(
            Cats, "Коты", PackKind.CustomEmoji, [new NewSticker(new UploadedFile("file-1"), ["😂", "🐱"])], Cancel);

        pack.Stickers.Select(sticker => sticker.UniqueId).Should().Equal("AgADc2", "AgADc3");
        SameJson(_service.Single("POST /v1/packs").Body,
            """{"name":"cats_by_StickerSnapAppBot","title":"Коты","kind":"emoji","stickers":[{"file":"file-1","emoji":["😂","🐱"]}]}""");
    }

    [Fact]
    public async Task Copies_a_pack_and_keeps_the_format_of_each_sticker()
    {
        _service.Answer("POST /v1/packs/copies", """
            {"name":"cats_by_StickerSnapAppBot","title":"Коты","kind":"stickers","stickers":[
            {"id":"F1","uniqueId":"U1","emoji":["😂"],"format":"static"},
            {"id":"F2","uniqueId":"U2","emoji":["😴"],"format":"animated"},
            {"id":"F3","uniqueId":"U3","emoji":["🔥"]}]}
            """, HttpStatusCode.Created);

        var pack = await Publisher.CopyPackAsync("OldCats", Cats, "Коты", Cancel);

        pack.Stickers.Select(sticker => sticker.Format).Should().Equal(StickerFormat.Static, StickerFormat.Animated, StickerFormat.Video);
        SameJson(_service.Single("POST /v1/packs/copies").Body,
            """{"source":"OldCats","name":"cats_by_StickerSnapAppBot","title":"Коты"}""");
    }

    [Fact]
    public async Task Adds_and_replaces_by_the_pack_and_the_old_stickers_id()
    {
        _service.Answer($"POST /v1/packs/{Cats}/stickers", PackJson).Answer($"PUT /v1/packs/{Cats}/stickers/CAAC-old", PackJson);
        var sticker = new NewSticker(new UploadedFile("file-2"), ["🔥"]);

        var added = await Publisher.AddStickerAsync(Cats, sticker, Cancel);
        var replaced = await Publisher.ReplaceStickerAsync(Cats, "CAAC-old", sticker, Cancel);

        added.Name.Should().Be(Cats);
        replaced.Name.Should().Be(Cats);
        SameJson(_service.Single($"PUT /v1/packs/{Cats}/stickers/CAAC-old").Body, """{"file":"file-2","emoji":["🔥"]}""");
    }

    [Fact]
    public async Task Reads_a_pack_that_is_not_the_users_as_none()
    {
        _service.Refuse($"GET /v1/packs/{Cats}", 404, "pack_not_found");

        (await Publisher.GetPackAsync(Cats, Cancel)).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Asks_the_service_whether_a_name_is_taken(bool taken)
    {
        _service.Answer($"GET /v1/pack-names/{Cats}", $$"""{"name":"{{Cats}}","taken":{{(taken ? "true" : "false")}}}""");

        (await Publisher.IsNameTakenAsync(Cats, Cancel)).Should().Be(taken);
    }

    [Fact]
    public async Task Deletes_a_sticker_out_of_its_pack_and_downloads_a_file()
    {
        _service.Answer("GET /v1/stickers/CAAC-1/file", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var path = Path.Combine(_files.FullName, "down.webm");

        await Publisher.DeleteStickerAsync(Cats, "CAAC-1", Cancel);
        await Publisher.DownloadAsync("CAAC-1", path, Cancel);

        _service.Single($"DELETE /v1/packs/{Cats}/stickers/CAAC-1");
        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(1, 2, 3);
    }

    /// <summary>The same JSON, however its strings are escaped.</summary>
    private static void SameJson(string actual, string expected) =>
        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)).Should().BeTrue(actual);
}
