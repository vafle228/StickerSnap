using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.Render;

/// <summary>
/// Renders the default sticker of every clip of a corpus in each of the given ways and reads each
/// result back: the size, what the file holds, how long it took, what Telegram would reject.
/// The stage 0 render spike, and later the regression run of QA over the same corpus.
/// </summary>
public sealed class CorpusRenderService(IMediaProbe probe, IStickerRenderer renderer, TimeProvider time)
{
    public async Task<CorpusReport> RunAsync(
        IReadOnlyList<string> sources,
        string outputDirectory,
        IReadOnlyList<RenderMode> modes,
        IProgress<CorpusEntry>? progress,
        CancellationToken cancellationToken)
    {
        var limits = TelegramLimits.Current.Sticker;
        List<CorpusEntry> entries = [];
        foreach (var source in sources)
        {
            var entry = await RenderAsync(source, outputDirectory, modes, limits, cancellationToken);
            entries.Add(entry);
            progress?.Report(entry);
        }

        return new CorpusReport(limits, modes, entries);
    }

    private async Task<CorpusEntry> RenderAsync(
        string source,
        string outputDirectory,
        IReadOnlyList<RenderMode> modes,
        PresetLimits limits,
        CancellationToken cancellationToken)
    {
        MediaInfo media;
        try
        {
            media = await probe.ProbeAsync(source, cancellationToken);
        }
        catch (AppException e)
        {
            return new CorpusEntry(source, null, null, [], Failure.Of(e));
        }

        StickerSpec spec;
        try
        {
            spec = StickerSpec.Default(media, limits);
        }
        catch (ArgumentException e)
        {
            return new CorpusEntry(source, media, null, [], new Failure(e.Message, null));
        }

        List<ModeOutcome> outcomes = [];
        foreach (var mode in modes)
            outcomes.Add(await RenderAsync(source, media, spec, mode, outputDirectory, limits, cancellationToken));
        return new CorpusEntry(source, media, spec, outcomes, null);
    }

    private async Task<ModeOutcome> RenderAsync(
        string source,
        MediaInfo media,
        StickerSpec spec,
        RenderMode mode,
        string outputDirectory,
        PresetLimits limits,
        CancellationToken cancellationToken)
    {
        var rate = mode.RateFor(spec, limits);
        var output = Path.Combine(outputDirectory, $"{Path.GetFileName(source)}.{mode.Name}.webm");
        var started = time.GetTimestamp();
        try
        {
            var rendered = await renderer.RenderAsync(
                new StickerRender(source, media, spec, rate, mode.Effort, output), cancellationToken);
            var elapsed = time.GetElapsedTime(started);
            var sticker = await probe.ProbeAsync(rendered.Path, cancellationToken);
            return new ModeOutcome(mode, rate, elapsed, rendered.SizeBytes, sticker, StickerCheck.Problems(sticker, limits), null);
        }
        catch (AppException e)
        {
            return new ModeOutcome(mode, rate, time.GetElapsedTime(started), null, null, [], Failure.Of(e));
        }
    }
}

/// <summary>One way to render the corpus: an effort and a way to spend bits.</summary>
/// <param name="Name">For the report and the file names: `crf`, `2pass`</param>
/// <param name="Crf">For a one-pass mode; null renders in two passes at <see cref="TargetBitrate"/></param>
public sealed record RenderMode(string Name, RenderEffort Effort, int? Crf)
{
    public RateControl RateFor(StickerSpec spec, PresetLimits limits) => Crf is { } crf
        ? new RateControl.ConstantQuality(crf)
        : new RateControl.TwoPass(TargetBitrate.BitsPerSecond(limits.MaxBytes, spec.DurationSec, spec.KeepAlpha));
}

public sealed record CorpusReport(PresetLimits Limits, IReadOnlyList<RenderMode> Modes, IReadOnlyList<CorpusEntry> Entries);

/// <param name="Source">Null when the probe failed</param>
/// <param name="Spec">Null when the clip could not be read</param>
public sealed record CorpusEntry(
    string SourcePath,
    MediaInfo? Source,
    StickerSpec? Spec,
    IReadOnlyList<ModeOutcome> Outcomes,
    Failure? Failure);

/// <param name="SizeBytes">Null when the render failed</param>
/// <param name="Sticker">What the probe read from the rendered file</param>
/// <param name="Problems">What Telegram would reject, from <see cref="StickerCheck"/></param>
public sealed record ModeOutcome(
    RenderMode Mode,
    RateControl Rate,
    TimeSpan Elapsed,
    long? SizeBytes,
    MediaInfo? Sticker,
    IReadOnlyList<string> Problems,
    Failure? Failure)
{
    public bool Passes => Failure is null && Sticker is not null && Problems.Count == 0;

    /// <summary>The source could be transparent and the sticker still can; true for an opaque source.</summary>
    public bool KeptAlpha(MediaInfo source) => !source.HasAlpha || Sticker?.HasAlpha == true;
}

/// <param name="Log">The media engine's words, when it had any</param>
public sealed record Failure(string Message, string? Log)
{
    public static Failure Of(AppException e) => new($"{e.Code}: {e.Message}", e.Log);
}
