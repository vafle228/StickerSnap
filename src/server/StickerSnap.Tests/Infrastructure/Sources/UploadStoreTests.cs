using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Sources;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Sources;

/// <summary>Uploads in a temporary data directory: kept whole or not at all, taken by a project, cleared after a day.</summary>
public sealed class UploadStoreTests : IDisposable
{
    private static readonly byte[] Head = [1, 2, 3];
    private static readonly byte[] Rest = [.. Enumerable.Range(0, 300_000).Select(i => (byte)i)];

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-uploads-");
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly UploadOptions _options = new();
    private readonly UploadStore _store;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public UploadStoreTests()
    {
        _store = new UploadStore(
            Options.Create(new DataOptions { DataDir = _data.FullName }),
            Options.Create(_options),
            _clock,
            NullLogger<UploadStore>.Instance);
    }

    public void Dispose() => _data.Delete(recursive: true);

    private Task<string> SaveAsync(string name = "cat.gif", Stream? rest = null) =>
        _store.SaveAsync(name, Head, rest ?? new MemoryStream(Rest), Cancel);

    private string[] FilesLeft() => [.. Directory.EnumerateFiles(_data.FullName, "*", SearchOption.AllDirectories)];

    [Fact]
    public async Task Keeps_the_head_and_the_rest_under_the_name_given()
    {
        var id = await SaveAsync();

        var path = _store.Find(id);
        Path.GetFileName(path).Should().Be("cat.gif");
        (await File.ReadAllBytesAsync(path!, Cancel)).Should().Equal([.. Head, .. Rest]);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("../../projects")]
    [InlineData("5d0e7a3b-9f12-4f7a-9c21-6f1c2a4e8b3d")]
    public void Finds_nothing_for_an_id_it_did_not_give(string id)
    {
        _store.Find(id).Should().BeNull();
    }

    [Fact]
    public async Task Leaves_nothing_of_a_file_over_the_limit()
    {
        _options.MaxBytes = 100_000;

        var save = () => SaveAsync();

        var exception = (await save.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCode.BadRequest);
        exception.Fields["file"].Should().Equal("too_large");
        FilesLeft().Should().BeEmpty();
        Directory.EnumerateDirectories(_store.UploadsDir).Should().BeEmpty();
    }

    [Fact]
    public async Task Leaves_nothing_of_a_dropped_request()
    {
        var save = () => SaveAsync(rest: new DroppedStream(Rest[..70_000]));

        await save.Should().ThrowAsync<OperationCanceledException>();
        FilesLeft().Should().BeEmpty();
    }

    [Fact]
    public async Task Moves_a_taken_upload_where_it_is_not_cleared_and_forgets_its_id()
    {
        var id = await SaveAsync();

        var kept = await _store.KeepAsync(id, Cancel);

        kept.Should().StartWith(_store.SourcesDir);
        Path.GetFileName(kept).Should().Be("cat.gif");
        (await File.ReadAllBytesAsync(kept, Cancel)).Should().Equal([.. Head, .. Rest]);
        _store.Find(id).Should().BeNull();
        var again = () => _store.KeepAsync(id, Cancel);
        (await again.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }

    [Fact]
    public async Task Clears_uploads_nobody_took_for_a_day_but_never_taken_ones()
    {
        var old = await SaveAsync("old.gif");
        var taken = await _store.KeepAsync(await SaveAsync("taken.gif"), Cancel);
        var recent = await SaveAsync("recent.gif");
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(_store.Find(old)!)!, _clock.GetUtcNow().UtcDateTime.AddHours(-25));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(taken)!, _clock.GetUtcNow().UtcDateTime.AddDays(-30));

        await SaveAsync("new.gif");

        _store.Find(old).Should().BeNull();
        _store.Find(recent).Should().NotBeNull();
        File.Exists(taken).Should().BeTrue();
    }

    /// <summary>A request body whose connection drops after some bytes.</summary>
    private sealed class DroppedStream(byte[] before) : MemoryStream(before)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new IOException("The client closed the connection");
        }
    }

    [Fact]
    public async Task Reserves_an_upload_the_server_fills_itself()
    {
        var slot = _store.Reserve("Кот смотрит в окно.webm");
        await File.WriteAllBytesAsync(slot.Path, Rest, Cancel);

        _store.Find(slot.UploadId).Should().Be(slot.Path);
        var kept = await _store.KeepAsync(slot.UploadId, Cancel);
        Path.GetFileName(kept).Should().Be("Кот смотрит в окно.webm");
    }

    [Fact]
    public async Task Discards_an_upload_with_whatever_was_written_into_it()
    {
        var slot = _store.Reserve("cat.webm");
        await File.WriteAllBytesAsync(slot.Path + ".part", Head, Cancel);

        _store.Discard(slot.UploadId);
        _store.Discard("not-a-guid");

        FilesLeft().Should().BeEmpty();
        _store.Find(slot.UploadId).Should().BeNull();
    }

    [Fact]
    public void Tells_the_room_left_on_its_disk()
    {
        _store.FreeBytes().Should().BePositive();
    }
}
