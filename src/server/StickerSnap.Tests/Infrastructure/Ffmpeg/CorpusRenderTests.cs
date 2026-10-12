using StickerSnap.Application.Render;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The test corpus of TestData/Corpus (30 GIFs from Giphy, a third of them transparent stickers)
/// rendered as the default sticker in one two-pass attempt by the real FFmpeg. The regression run
/// of the stage 0 render spike: it holds the gate, 90% of the corpus in 256 KB; clips under a
/// second still overshoot by a few percent until the fit ladder (B5) adds a second attempt.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class CorpusRenderTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fits_90_percent_of_the_corpus_into_stickers_Telegram_takes_in_two_passes()
    {
        var corpus = TestFiles.In("Corpus");
        corpus.Should().HaveCount(30, "TestData/Corpus holds the test corpus");
        var directory = Directory.CreateDirectory(ffmpeg.Clip("corpus")).FullName;
        List<string> sources = [];
        foreach (var clip in corpus) sources.Add(await TestFiles.CopyToAsync(clip, directory, Cancel));
        var service = new CorpusRenderService(ffmpeg.Probe, new FfmpegStickerRenderer(ffmpeg.Ffmpeg), TimeProvider.System);

        var report = await service.RunAsync(
            sources, Path.Combine(directory, "out"), [new RenderMode("2pass", RenderEffort.Final, null)], null, Cancel);

        report.Entries.Should().AllSatisfy(entry =>
        {
            entry.Failure.Should().BeNull(entry.SourcePath);
            var outcome = entry.Outcomes.Single();
            outcome.Failure.Should().BeNull(entry.SourcePath);
            outcome.KeptAlpha(entry.Source!).Should().BeTrue(entry.SourcePath);
        });
        var failing = report.Entries.Where(e => !e.Outcomes.Single().Passes)
            .Select(e => $"{Path.GetFileName(e.SourcePath)}: {string.Join("; ", e.Outcomes.Single().Problems)}");
        failing.Should().HaveCountLessThanOrEqualTo(3, "the stage 0 gate is 90% of the corpus");
    }
}
