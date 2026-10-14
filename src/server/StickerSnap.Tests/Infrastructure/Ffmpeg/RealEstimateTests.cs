using StickerSnap.Application.Estimates;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Render;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Webm;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The size forecast over the real FFmpeg on clips of the corpus, against what the fit spike found
/// (12 October: only 12-anime gives something up, fps 30 → 24) and against the export itself, so
/// «≈ 330 КБ» in the inspector and the export's ladder do not disagree.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealEstimateTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>, IDisposable
{
    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-real-estimate-");
    private readonly IProjectService _projects = Substitute.For<IProjectService>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _files.Delete(recursive: true);

    /// <summary>A project of the corpus clip as a new project has it: its first 3 s in the middle square.</summary>
    private async Task<Project> ProjectAsync(string clip)
    {
        var path = await TestFiles.CopyToAsync($"Corpus/{clip}", _files.FullName, Cancel);
        var media = await ffmpeg.Probe.ProbeAsync(path, Cancel);
        var project = new Project(
            Guid.NewGuid(), clip, Revision: 1, DateTimeOffset.UtcNow,
            new ProjectSource(path, clip, $"v1:{clip}", media), DefaultScene.For(media));
        _projects.GetAsync(project.Id, Arg.Any<CancellationToken>()).Returns(new OpenedProject(project, path + ".ssproj", PresenceStatus.Ok));
        return project;
    }

    [Theory]
    [InlineData("01-cat.gif")]
    [InlineData("17-football.gif")]
    [InlineData("25-sticker-heart.gif")]
    public async Task Says_a_clip_the_spike_fitted_without_concessions_fits(string clip)
    {
        var project = await ProjectAsync(clip);
        using var service = new EstimateService(_projects, new FfmpegStickerRenderer(ffmpeg.Ffmpeg));

        var estimate = await service.EstimateAsync(project.Id, project.Scene.Segments[0], Cancel);

        estimate.Fits.Should().BeTrue($"{clip} weighs {estimate.Bytes} B at CRF 40");
        estimate.Bytes.Should().BePositive();
        estimate.LikelyConcessions.Should().BeEmpty();
        estimate.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task Agrees_with_the_export_on_the_size_and_on_what_is_given_up()
    {
        var project = await ProjectAsync("12-anime.gif");
        var segment = project.Scene.Segments[0];
        var renderer = new FfmpegStickerRenderer(ffmpeg.Ffmpeg);
        using var service = new EstimateService(_projects, renderer);
        var engine = new SegmentRenderService(new StickerFitService(renderer), renderer, ffmpeg.Probe, new WebmStickerHeader());

        var estimate = await service.EstimateAsync(project.Id, segment, Cancel);
        var export = await engine.RenderAsync(
            new SegmentRender(project.Source.Path, project.Source.Media, project.Scene, segment.Id, RenderEffort.Final, Path.Combine(_files.FullName, "12-anime.webm")),
            progress: null,
            Cancel);

        estimate.Fits.Should().BeFalse($"12-anime weighs {estimate.Bytes} B at CRF 40");
        estimate.LikelyConcessions.Should().Equal(new Concession.Fps(30, 24));
        export.Concessions.Should().Equal(estimate.LikelyConcessions);
        // The same draft measures the first step for both; an encoder's threads may move it a little
        estimate.Bytes.Should().BeCloseTo(export.Ladder[0].Bytes, (ulong)(export.Ladder[0].Bytes / 50));
    }

    [Fact]
    public async Task Suggests_edits_after_which_the_sticker_fits()
    {
        var project = await ProjectAsync("12-anime.gif");
        var segment = project.Scene.Segments[0];
        using var service = new EstimateService(_projects, new FfmpegStickerRenderer(ffmpeg.Ffmpeg));

        var estimate = await service.EstimateAsync(project.Id, segment, Cancel);

        estimate.Suggestions.Should().NotBeEmpty();
        foreach (var suggestion in estimate.Suggestions)
        {
            var edited = await service.EstimateAsync(project.Id, suggestion.ApplyTo(segment), Cancel);
            edited.Fits.Should().BeTrue($"after {suggestion} the sticker weighs {edited.Bytes} B");
        }
    }
}
