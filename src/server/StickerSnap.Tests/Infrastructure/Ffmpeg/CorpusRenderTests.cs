using StickerSnap.Application.Render;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The test corpus of TestData/Corpus (30 GIFs from Giphy, a third of them transparent stickers)
/// rendered as the default sticker through the fit ladder by the real FFmpeg. The regression run
/// of the stage 0 render and fit spikes: the gate asks for 90% of the corpus in 256 KB, and the
/// ladder, which renders again what overshoots, brings every clip in.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class CorpusRenderTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fits_the_whole_corpus_into_stickers_Telegram_takes_through_the_fit_ladder()
    {
        var corpus = TestFiles.In("Corpus");
        corpus.Should().HaveCount(30, "TestData/Corpus holds the test corpus");
        var directory = Directory.CreateDirectory(ffmpeg.Clip("corpus")).FullName;
        List<string> sources = [];
        foreach (var clip in corpus) sources.Add(await TestFiles.CopyToAsync(clip, directory, Cancel));
        var renderer = new FfmpegStickerRenderer(ffmpeg.Ffmpeg);
        var service = new CorpusRenderService(ffmpeg.Probe, renderer, new StickerFitService(renderer), TimeProvider.System);

        var report = await service.RunAsync(
            sources, Path.Combine(directory, "out"), [new RenderMode("fit", RenderEffort.Final, null, Fit: true)], null, Cancel);

        report.Entries.Should().AllSatisfy(entry =>
        {
            entry.Failure.Should().BeNull(entry.SourcePath);
            var outcome = entry.Outcomes.Single();
            outcome.Failure.Should().BeNull(entry.SourcePath);
            outcome.KeptAlpha(entry.Source!).Should().BeTrue(entry.SourcePath);
        });
        var failing = report.Entries.Where(e => !e.Outcomes.Single().Passes)
            .Select(e => $"{Path.GetFileName(e.SourcePath)}: {string.Join("; ", e.Outcomes.Single().Problems)}");
        failing.Should().BeEmpty("the fit ladder renders again what overshoots; the stage 0 gate is 90% of the corpus");
    }
}
