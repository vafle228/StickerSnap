using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Infrastructure.Files;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Files;

/// <summary>
/// Drafts and exported stickers in a temporary data directory: served by id, drafts cleared after an
/// hour, exports' ids kept over a restart.
/// </summary>
public sealed class RenderedFileStoreTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-files-");
    private readonly DirectoryInfo _stickers = Directory.CreateTempSubdirectory("stickersnap-stickers-");
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _data.Delete(recursive: true);
        _stickers.Delete(recursive: true);
    }

    private RenderedFileStore NewStore() => new(
        Options.Create(new DataOptions { DataDir = _data.FullName }), _clock, NullLogger<RenderedFileStore>.Instance);

    private async Task<string> StickerAsync(string name)
    {
        var path = Path.Combine(_stickers.FullName, name);
        await File.WriteAllBytesAsync(path, [1, 2, 3], Cancel);
        return path;
    }

    [Fact]
    public async Task Serves_a_draft_once_it_is_written()
    {
        var store = NewStore();

        var draft = store.NewDraft();
        var before = await store.FindAsync(draft.Id, Cancel);
        await File.WriteAllBytesAsync(draft.Path, [1, 2, 3], Cancel);

        before.Should().BeNull();
        (await store.FindAsync(draft.Id, Cancel)).Should().Be(draft.Path);
        Path.GetDirectoryName(draft.Path).Should().Be(store.DraftsDir);
    }

    [Fact]
    public async Task Clears_drafts_older_than_an_hour_when_a_new_one_is_asked_for()
    {
        var store = NewStore();
        var old = store.NewDraft();
        await File.WriteAllBytesAsync(old.Path, [1], Cancel);
        var recent = store.NewDraft();
        await File.WriteAllBytesAsync(recent.Path, [2], Cancel);
        File.SetLastWriteTimeUtc(old.Path, _clock.GetUtcNow().UtcDateTime - RenderedFileStore.DraftLifetime);
        File.SetLastWriteTimeUtc(recent.Path, _clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(59));

        store.NewDraft();

        File.Exists(old.Path).Should().BeFalse();
        (await store.FindAsync(old.Id, Cancel)).Should().BeNull();
        (await store.FindAsync(recent.Id, Cancel)).Should().Be(recent.Path);
    }

    [Fact]
    public async Task Serves_an_exported_sticker_by_its_id_after_a_restart()
    {
        var sticker = await StickerAsync("Кот — Стикер 1.webm");
        var id = await NewStore().AddAsync(sticker, Cancel);

        var found = await NewStore().FindAsync(id, Cancel);

        found.Should().Be(sticker);
        File.Exists(Path.Combine(_data.FullName, RenderedFileStore.TableName)).Should().BeTrue();
    }

    [Fact]
    public async Task Gives_each_export_its_own_id_and_forgets_one_removed()
    {
        var store = NewStore();
        var sticker = await StickerAsync("Кот — Стикер 1.webm");
        var first = await store.AddAsync(sticker, Cancel);
        var second = await store.AddAsync(sticker, Cancel);

        await store.RemoveAsync(first, Cancel);
        await store.RemoveAsync("never-was", Cancel);

        second.Should().NotBe(first);
        (await NewStore().FindAsync(first, Cancel)).Should().BeNull();
        (await NewStore().FindAsync(second, Cancel)).Should().Be(sticker);
        File.Exists(sticker).Should().BeTrue();
    }

    [Fact]
    public async Task Knows_no_file_once_the_user_removed_it()
    {
        var store = NewStore();
        var sticker = await StickerAsync("Кот — Стикер 1.webm");
        var id = await store.AddAsync(sticker, Cancel);

        File.Delete(sticker);

        (await store.FindAsync(id, Cancel)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("../files.json")]
    [InlineData("0f8e1d2c3b4a59687766554433221100")]
    public async Task Knows_no_file_by_an_id_it_never_gave(string id)
    {
        (await NewStore().FindAsync(id, Cancel)).Should().BeNull();
    }

    [Fact]
    public async Task Starts_with_no_exports_when_the_table_is_broken()
    {
        await File.WriteAllTextAsync(Path.Combine(_data.FullName, RenderedFileStore.TableName), "{ not json", Cancel);
        var store = NewStore();

        var id = await store.AddAsync(await StickerAsync("Кот — Стикер 1.webm"), Cancel);

        (await NewStore().FindAsync(id, Cancel)).Should().NotBeNull();
    }
}
