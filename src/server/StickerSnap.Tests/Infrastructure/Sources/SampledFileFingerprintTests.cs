using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Sources;

namespace StickerSnap.Tests.Infrastructure.Sources;

/// <summary>The mark a project keeps of its video's content.</summary>
public sealed class SampledFileFingerprintTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("stickersnap-fingerprint-");
    private readonly SampledFileFingerprint _fingerprint = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Delete(recursive: true);

    private async Task<string> OfAsync(string name, byte[] content)
    {
        var path = Path.Combine(_folder.FullName, name);
        await File.WriteAllBytesAsync(path, content, Cancel);
        return await _fingerprint.OfAsync(path, Cancel);
    }

    private static byte[] Video(int length, byte fill = 7) => [.. Enumerable.Repeat(fill, length)];

    [Fact]
    public async Task Is_the_same_for_the_same_content_under_another_name()
    {
        var content = Video(3 * SampledFileFingerprint.SampleBytes);

        (await OfAsync("a.mp4", content)).Should().Be(await OfAsync("b.mp4", content));
    }

    [Fact]
    public async Task Starts_with_its_version_and_the_size()
    {
        (await OfAsync("a.gif", Video(1234))).Should().StartWith("v1:1234:");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Changes_when_the_start_or_the_end_changes(int index)
    {
        var content = Video(3 * SampledFileFingerprint.SampleBytes);
        var changed = content.ToArray();
        changed[index < 0 ? changed.Length - 1 : index] = 8;

        (await OfAsync("a.mp4", changed)).Should().NotBe(await OfAsync("b.mp4", content));
    }

    [Fact]
    public async Task Changes_with_the_size()
    {
        (await OfAsync("a.mp4", Video(5000))).Should().NotBe(await OfAsync("b.mp4", Video(5001)));
    }

    [Fact]
    public async Task Finds_no_file_that_is_not_there()
    {
        var read = () => _fingerprint.OfAsync(Path.Combine(_folder.FullName, "gone.mp4"), Cancel);

        (await read.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }
}
