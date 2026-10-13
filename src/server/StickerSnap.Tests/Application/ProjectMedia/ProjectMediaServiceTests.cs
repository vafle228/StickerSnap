using StickerSnap.Application.Media;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.ProjectMedia;

/// <summary>
/// The video, the cover and the frames of a project over a media engine and a cache that only
/// pretend: what is made, from which file, under which key, and when the video must be there.
/// </summary>
public sealed class ProjectMediaServiceTests
{
    private static readonly Project Cat = SampleProjects.Sample;

    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly IEditorMedia _media = Substitute.For<IEditorMedia>();
    private readonly IMediaCache _cache = Substitute.For<IMediaCache>();
    private readonly List<string> _made = [];
    private readonly ProjectMediaService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ProjectMediaServiceTests()
    {
        Opens(Cat, PresenceStatus.Ok);
        _cache.Find(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        // The cache makes every file it is asked for at /cache/<key>.<ext>
        _cache.GetOrAddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var path = $"/cache/{call.ArgAt<string>(0)}.{call.ArgAt<string>(1)}";
                await call.ArgAt<Func<string, CancellationToken, Task>>(2)(path, CancellationToken.None);
                _made.Add(path);
                return path;
            });
        _service = new ProjectMediaService(_projects, _media, _cache);
    }

    private void Opens(Project project, PresenceStatus source) =>
        _projects.GetAsync(project.Id, Arg.Any<CancellationToken>()).Returns(new OpenedProject(project, "/projects/cat.ssproj", source));

    private static Project WithMedia(MediaInfo media) => Cat with { Source = Cat.Source with { Media = media } };

    [Fact]
    public async Task Gives_a_gif_as_a_playable_copy_made_from_the_source()
    {
        var video = await _service.VideoAsync(Cat.Id, Cancel);

        video.ContentType.Should().Be("video/webm");
        video.Path.Should().Be(_made.Should().ContainSingle().Which);
        await _media.Received(1).WritePlayableAsync(Cat.Source.Path, Cat.Source.Media, video.Path, Arg.Any<CancellationToken>());
        video.Tag.Should().Be(Path.GetFileNameWithoutExtension(video.Path));
    }

    [Fact]
    public async Task Gives_a_video_the_browser_plays_as_it_is()
    {
        var mp4 = WithMedia(Cat.Source.Media with { Format = MediaFormat.Mp4, Codec = "h264" });
        Opens(mp4, PresenceStatus.Ok);

        var video = await _service.VideoAsync(Cat.Id, Cancel);

        video.Should().Be(new MediaFile(Cat.Source.Path, "video/mp4", MediaKeys.Of("source", Cat.Source.Fingerprint)));
        _made.Should().BeEmpty();
    }

    [Fact]
    public async Task Keys_the_copy_by_the_source_s_content_so_a_new_video_gets_its_own()
    {
        var first = await _service.VideoAsync(Cat.Id, Cancel);
        Opens(Cat with { Source = Cat.Source with { Fingerprint = "v1:2048:another" } }, PresenceStatus.Ok);

        var second = await _service.VideoAsync(Cat.Id, Cancel);

        second.Tag.Should().NotBe(first.Tag);
    }

    [Fact]
    public async Task Says_the_video_is_gone_rather_than_making_anything()
    {
        Opens(Cat, PresenceStatus.Missing);

        var video = () => _service.VideoAsync(Cat.Id, Cancel);
        var frames = () => _service.FramesAsync(Cat.Id, 0, 30, 96, Cancel);

        (await video.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
        (await frames.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
        _made.Should().BeEmpty();
    }

    [Fact]
    public async Task Makes_the_strip_the_query_asks_for()
    {
        var frames = await _service.FramesAsync(Cat.Id, 1, 30, 96, Cancel);

        frames.ContentType.Should().Be("image/webp");
        await _media.Received(1).WriteStripAsync(
            Cat.Source.Path,
            Cat.Source.Media,
            Arg.Is<FrameStrip>(strip => strip.From == 1 && strip.Count == 30 && strip.Height == 96 && strip.FrameWidth == 171),
            frames.Path,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_strip_past_the_source_s_end()
    {
        // The sample GIF has 31 frames
        var frames = () => _service.FramesAsync(Cat.Id, 30, 30, 96, Cancel);

        (await frames.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BadRequest);
        _made.Should().BeEmpty();
    }

    [Fact]
    public async Task Gives_strips_of_other_frames_or_heights_their_own_files()
    {
        var one = await _service.FramesAsync(Cat.Id, 0, 30, 96, Cancel);
        var other = await _service.FramesAsync(Cat.Id, 1, 30, 96, Cancel);
        var taller = await _service.FramesAsync(Cat.Id, 0, 30, 144, Cancel);

        new[] { one.Tag, other.Tag, taller.Tag }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Makes_the_cover_of_the_first_sticker()
    {
        var cover = await _service.CoverAsync(Cat.Id, Cancel);

        cover.ContentType.Should().Be("image/webp");
        await _media.Received(1).WriteCoverAsync(Cat.Source.Path, Cat.Source.Media, CoverShot.Of(Cat), cover.Path, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Makes_a_new_cover_when_the_first_sticker_changes()
    {
        var before = await _service.CoverAsync(Cat.Id, Cancel);
        Opens(Cat with { Scene = Cat.Scene with { Segments = [Cat.Scene.Segments[0] with { InFrame = 10 }] } }, PresenceStatus.Ok);

        var after = await _service.CoverAsync(Cat.Id, Cancel);

        after.Tag.Should().NotBe(before.Tag);
    }

    [Fact]
    public async Task Keeps_showing_a_cover_made_before_the_video_went_missing()
    {
        Opens(Cat, PresenceStatus.Missing);
        var key = MediaKeys.Of("cover", Cat.Source.Fingerprint, CoverShot.Of(Cat).Frame, CoverShot.Of(Cat).Crop, CoverShot.Of(Cat).Size);
        _cache.Find(key, "webp").Returns("/cache/kept.webp");

        var cover = await _service.CoverAsync(Cat.Id, Cancel);

        cover.Should().Be(new MediaFile("/cache/kept.webp", "image/webp", key));
        _made.Should().BeEmpty();
    }

    [Fact]
    public async Task Says_the_video_is_gone_for_a_cover_never_made()
    {
        Opens(Cat, PresenceStatus.Missing);

        var cover = () => _service.CoverAsync(Cat.Id, Cancel);

        (await cover.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }

    [Fact]
    public async Task Passes_on_a_project_that_is_not_there()
    {
        var missing = Guid.NewGuid();
        _projects.GetAsync(missing, Arg.Any<CancellationToken>())
            .Returns<OpenedProject>(_ => throw new AppException(ErrorCode.ProjectNotFound, "no such project"));

        var video = () => _service.VideoAsync(missing, Cancel);

        (await video.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectNotFound);
    }
}
