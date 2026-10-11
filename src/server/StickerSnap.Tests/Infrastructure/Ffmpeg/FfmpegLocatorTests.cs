using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>The search over real directories; `-version` is answered by a substitute, no process runs.</summary>
public sealed class FfmpegLocatorTests : IDisposable
{
    private static readonly string Executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stickersnap-ffmpeg-");
    private readonly IFfmpegVersionReader _reader = Substitute.For<IFfmpegVersionReader>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    private FfmpegLocator Locator(string? configured) =>
        new(Options.Create(new FfmpegOptions { FfmpegDir = configured }), _reader, NullLogger<FfmpegLocator>.Instance);

    /// <summary>A directory with an ffmpeg file that answers `-version` with <paramref name="version"/>.</summary>
    private string Ffmpeg(string name, string? version)
    {
        var directory = _root.CreateSubdirectory(name).FullName;
        var executable = Path.Combine(directory, Executable);
        File.WriteAllText(executable, "");
        _reader.ReadAsync(executable, Arg.Any<CancellationToken>())
            .Returns(version is null ? null : $"ffmpeg version {version} Copyright (c) 2000-2026 the FFmpeg developers\n");
        return directory;
    }

    [Fact]
    public void Looks_in_the_configured_directory_then_next_to_the_server_then_on_path()
    {
        var path = string.Join(Path.PathSeparator, "/usr/local/bin", "", " /usr/bin ");

        var directories = FfmpegLocator.Directories("tools/ffmpeg", "/opt/stickersnap", path);

        directories.Should().Equal(Path.GetFullPath("tools/ffmpeg"), "/opt/stickersnap", "/usr/local/bin", "/usr/bin");
    }

    [Fact]
    public void Skips_an_unset_directory_and_an_empty_path()
    {
        FfmpegLocator.Directories(null, "/opt/stickersnap", null).Should().Equal("/opt/stickersnap");
    }

    [Fact]
    public async Task Finds_ffmpeg_in_the_configured_directory()
    {
        var directory = Ffmpeg("configured", "n9.0.2-17-g2a571b6068");

        var lookup = await Locator(directory).FindAsync(Cancel);

        lookup.Status.Should().Be(FfmpegStatus.Ok);
        lookup.Version!.Text.Should().Be("9.0.2-17-g2a571b6068");
        lookup.Directory.Should().Be(directory);
    }

    [Fact]
    public async Task Tells_an_ffmpeg_older_than_9_0_apart_from_a_missing_one()
    {
        var lookup = await Locator(Ffmpeg("old", "7.1")).FindAsync(Cancel);

        lookup.Status.Should().Be(FfmpegStatus.TooOld);
        lookup.Version!.Text.Should().Be("7.1");
    }

    [Fact]
    public async Task Is_missing_when_the_configured_file_does_not_answer_as_ffmpeg()
    {
        var lookup = await Locator(Ffmpeg("broken", version: null)).FindAsync(Cancel);

        // An ffmpeg next to the test host or on this machine's PATH gets no answer either:
        // the substitute knows only the files the tests made
        lookup.Status.Should().Be(FfmpegStatus.Missing);
    }

    [Fact]
    public async Task Keeps_a_found_ffmpeg_for_the_life_of_the_process()
    {
        var locator = Locator(Ffmpeg("configured", "9.0"));

        await locator.FindAsync(Cancel);
        await locator.FindAsync(Cancel);

        await _reader.Received(1).ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Looks_again_while_ffmpeg_is_missing()
    {
        var directory = _root.CreateSubdirectory("installed-later").FullName;
        var locator = Locator(directory);

        var before = await locator.FindAsync(Cancel);
        var executable = Path.Combine(directory, Executable);
        File.WriteAllText(executable, "");
        _reader.ReadAsync(executable, Arg.Any<CancellationToken>()).Returns("ffmpeg version 9.0.2 Copyright\n");
        var after = await locator.FindAsync(Cancel);

        before.Status.Should().Be(FfmpegStatus.Missing);
        after.Status.Should().Be(FfmpegStatus.Ok);
    }
}
