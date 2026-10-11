using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// What the probe answers before it starts ffprobe; FFmpeg is missing here, so a test that got
/// that far would see `ffmpeg_missing`. Reading real files is in <see cref="RealFfmpegTests"/>.
/// </summary>
public sealed class FfprobeMediaProbeTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stickersnap-probe-");
    private readonly IFfmpegLocator _locator = Substitute.For<IFfmpegLocator>();
    private readonly IFfmpeg _ffmpeg = Substitute.For<IFfmpeg>();

    public FfprobeMediaProbeTests() =>
        _locator.FindAsync(Arg.Any<CancellationToken>()).Returns(FfmpegLookup.Missing);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    private FfprobeMediaProbe Probe() => new(_locator, _ffmpeg);

    private string File(string name)
    {
        var path = Path.Combine(_root.FullName, name);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    [Theory]
    [InlineData("clip.avi", "avi")]
    [InlineData("CLIP.MKV", "mkv")]
    [InlineData("clip", "")]
    public async Task Refuses_an_extension_of_another_format_by_name(string name, string extension)
    {
        var probe = () => Probe().ProbeAsync(File(name), Cancel);

        (await probe.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.FormatUnsupported && (string)e.Params["ext"] == extension);
    }

    [Fact]
    public async Task Tells_a_missing_file_by_its_own_code()
    {
        var probe = () => Probe().ProbeAsync(Path.Combine(_root.FullName, "gone.mp4"), Cancel);

        (await probe.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }

    [Fact]
    public async Task Needs_ffmpeg_for_a_file_it_would_read()
    {
        var probe = () => Probe().ProbeAsync(File("Clip.MP4"), Cancel);

        (await probe.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.FfmpegMissing);
    }
}
