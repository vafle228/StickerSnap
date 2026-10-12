using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.App;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Infrastructure.Sources;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Small clips made by FFmpeg, uploaded and read the way a new project reads its video: the format
/// told by the content is one the probe opens, and the default scene keeps every rule of the scene.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealSourceImportTests : IClassFixture<RealFfmpeg>, IDisposable
{
    private readonly RealFfmpeg _ffmpeg;
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-import-");
    private readonly UploadStore _uploads;
    private readonly SourceImport _import;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public RealSourceImportTests(RealFfmpeg ffmpeg)
    {
        _ffmpeg = ffmpeg;
        _uploads = new UploadStore(
            Options.Create(new DataOptions { DataDir = _data.FullName }),
            Options.Create(new UploadOptions()),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<UploadStore>.Instance);
        var host = Substitute.For<IHostInfo>();
        host.Mode.Returns(AppMode.Desktop);
        _import = new SourceImport(new SourceFiles(host, _uploads), ffmpeg.Probe, new SampledFileFingerprint(), _uploads);
    }

    public void Dispose() => _data.Delete(recursive: true);

    private Task<string> MakeAsync(string name, string source, params string[] output) =>
        _ffmpeg.MakeAsync(name, ["-f", "lavfi", "-i", source, .. output]);

    /// <summary>Uploads the clip under <paramref name="uploadName"/>, as a browser would send it.</summary>
    private async Task<string> UploadAsync(string clip, string uploadName)
    {
        await using var content = File.OpenRead(clip);
        return await new UploadService(_uploads).UploadAsync(uploadName, content, Cancel);
    }

    public static TheoryData<string, string, string[], MediaFormat> Clips => new()
    {
        { "clip.gif", "testsrc2=s=480x270:r=12:d=1.5", [], MediaFormat.Gif },
        { "clip.mp4", "testsrc2=s=640x360:r=30000/1001:d=4", ["-c:v", "libopenh264", "-b:v", "300k"], MediaFormat.Mp4 },
        { "clip.mov", "testsrc2=s=360x640:r=25:d=2", ["-c:v", "libopenh264", "-b:v", "300k", "-f", "mov"], MediaFormat.Mov },
        { "clip.webm", "testsrc2=s=512x512:r=30:d=2", ["-c:v", "libvpx-vp9", "-b:v", "150k"], MediaFormat.Webm },
        { "clip.webp", "testsrc2=s=200x100:r=10:d=1", ["-c:v", "libwebp_anim", "-loop", "0"], MediaFormat.Webp },
    };

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task Tells_each_upload_s_format_by_its_content_and_reads_it(string name, string source, string[] output, MediaFormat format)
    {
        var clip = await MakeAsync(name, source, output);
        // A name that lies: the content decides
        var uploadId = await UploadAsync(clip, "video.bin");

        var read = await _import.ReadAsync(new SourceRef.Upload(uploadId), Cancel);

        read.Media.Format.Should().Be(format);
        read.FileName.Should().Be($"video.{MediaFormats.ExtensionOf(format)}");
        read.Path.Should().StartWith(Path.Combine(_data.FullName, "sources"));
        File.Exists(read.Path).Should().BeTrue();
        read.Fingerprint.Should().StartWith($"v1:{new FileInfo(clip).Length}:");
    }

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task Opens_every_clip_with_a_default_scene_that_keeps_the_rules(string name, string source, string[] output, MediaFormat format)
    {
        var clip = await MakeAsync(name, source, output);

        var read = await _import.ReadAsync(new SourceRef.OnDisk(clip), Cancel);
        var scene = DefaultScene.For(read.Media);

        read.Media.Format.Should().Be(format);
        SceneValidator.Validate(scene, read.Media).Should().BeEmpty();
        var segment = scene.Segments.Single();
        ((segment.OutFrame + 1) / read.Media.Fps.ToDouble()).Should().BeLessThanOrEqualTo(3.0 + 1e-9);
    }

    [Fact]
    public async Task Takes_the_first_3_seconds_of_a_29_97_fps_video()
    {
        var clip = await MakeAsync("long.mp4", "testsrc2=s=640x360:r=30000/1001:d=8", "-c:v", "libopenh264", "-b:v", "300k");

        var read = await _import.ReadAsync(new SourceRef.OnDisk(clip), Cancel);

        DefaultScene.For(read.Media).Segments.Single().OutFrame.Should().Be(88);
    }
}
