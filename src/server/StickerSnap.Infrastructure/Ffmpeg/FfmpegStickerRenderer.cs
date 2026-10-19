using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Renders stickers with libvpx-vp9; two passes keep their statistics in a temp directory.</summary>
internal sealed class FfmpegStickerRenderer(IFfmpeg ffmpeg) : IStickerRenderer
{
    /// <summary>
    /// How much bigger a draft is than the final render at the same CRF: the medians over the test
    /// corpus at every step of the fit ladder (fit spike, 210 pairs). Opaque 1.22 (1.12–1.49 for
    /// 80% of clips), transparent 1.58 (1.35–1.78): libvpx's realtime mode codes the alpha stream
    /// worse than the picture.
    /// </summary>
    internal const double DraftOverFinal = 1.22;

    internal const double TransparentDraftOverFinal = 1.58;

    /// <summary>How the C library words ENOSPC, which ffmpeg prints when a write fails, on every system.</summary>
    private const string NoSpace = "No space left on device";

    public async Task<RenderedSticker> RenderAsync(StickerRender render, IProgress<PassProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(render.OutputPath))!, render);
            if (render.Rate is RateControl.TwoPass)
            {
                var passes = CreateTempDirectory("stickersnap-pass-", render);
                try
                {
                    var log = Path.Combine(passes.FullName, "vp9");
                    await RunAsync(Vp9StickerArguments.For(render, 1, log), 1, 2, render, progress, cancellationToken);
                    await RunAsync(Vp9StickerArguments.For(render, 2, log), 2, 2, render, progress, cancellationToken);
                }
                finally
                {
                    passes.Delete(recursive: true);
                }
            }
            else
            {
                await RunAsync(Vp9StickerArguments.For(render, 0, null), 1, 1, render, progress, cancellationToken);
            }
        }
        catch
        {
            // Half a file is no sticker: whatever stopped the render, nothing is left to pick up
            DeleteQuietly(render.OutputPath);
            throw;
        }

        return new RenderedSticker(render.OutputPath, new FileInfo(render.OutputPath).Length);
    }

    /// <summary>A draft at the probe's CRF, a fraction of a second, scaled to the final render's size.</summary>
    public async Task<long> EstimateAsync(StickerProbe probe, CancellationToken cancellationToken)
    {
        var scratch = Directory.CreateTempSubdirectory("stickersnap-probe-");
        try
        {
            var draft = await DraftAsync(probe, Path.Combine(scratch.FullName, "probe.webm"), cancellationToken);
            return draft.EstimatedBytes;
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    /// <summary>One realtime pass at the probe's CRF; the final render is that much smaller.</summary>
    public async Task<DraftSticker> DraftAsync(StickerProbe probe, string outputPath, CancellationToken cancellationToken)
    {
        var draft = new StickerRender(
            probe.SourcePath,
            probe.Source,
            probe.Spec,
            new RateControl.ConstantQuality(probe.Crf),
            RenderEffort.Draft,
            outputPath);
        var rendered = await RenderAsync(draft, progress: null, cancellationToken);
        var estimate = (long)Math.Round(rendered.SizeBytes / (probe.Spec.KeepAlpha ? TransparentDraftOverFinal : DraftOverFinal));
        return new DraftSticker(rendered.Path, rendered.SizeBytes, estimate);
    }

    /// <summary>`ffmpeg …` once, or twice for two passes, with the statistics beside the output.</summary>
    public string Describe(StickerRender render)
    {
        IEnumerable<IReadOnlyList<string>> runs = render.Rate is RateControl.TwoPass
            ? [Vp9StickerArguments.For(render, 1, render.OutputPath + ".vp9"), Vp9StickerArguments.For(render, 2, render.OutputPath + ".vp9")]
            : [Vp9StickerArguments.For(render, 0, null)];
        return string.Join(Environment.NewLine, runs.Select(arguments => string.Join(' ', ["ffmpeg", .. arguments.Select(FfmpegProcess.Quote)])));
    }

    private async Task RunAsync(
        IReadOnlyList<string> arguments,
        int pass,
        int passes,
        StickerRender render,
        IProgress<PassProgress>? progress,
        CancellationToken cancellationToken)
    {
        var frames = progress is null ? null : new FrameProgress(progress, pass, passes, render.Spec.PlayedFrames);
        frames?.Report(0);
        var result = await ffmpeg.RunAsync(arguments, frames, cancellationToken);
        if (result.IsSuccess)
        {
            frames?.Report(1);
            return;
        }

        var step = passes == 1 ? "the render" : $"pass {pass}";
        throw result.Log.Contains(NoSpace, StringComparison.OrdinalIgnoreCase)
            ? new AppException(ErrorCode.DiskFull, $"No space left for {step} of {Path.GetFileName(render.SourcePath)}") { Log = result.Log, Context = result.Context }
            : new AppException(
                ErrorCode.RenderFailed,
                $"ffmpeg failed {step} of {Path.GetFileName(render.SourcePath)} (exit code {result.ExitCode})")
            {
                Log = result.Log,
                Context = result.Context,
            };
    }

    /// <summary>Removes a file the failed render may have started, never hiding why it failed.</summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            // The directory may be gone or locked: there is no file to pick up either way
        }
    }

    private static void CreateDirectory(string path, StickerRender render)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            throw Unwritable(e, path, render);
        }
    }

    private static DirectoryInfo CreateTempDirectory(string prefix, StickerRender render)
    {
        try
        {
            return Directory.CreateTempSubdirectory(prefix);
        }
        catch (Exception e) when (IoErrors.IsIoError(e))
        {
            throw Unwritable(e, Path.GetTempPath(), render);
        }
    }

    private static AppException Unwritable(Exception e, string path, StickerRender render) => IoErrors.IsDiskFull(e)
        ? new AppException(ErrorCode.DiskFull, $"No space left in {path} to render {Path.GetFileName(render.SourcePath)}", e)
        : new AppException(ErrorCode.RenderFailed, $"Cannot write to {path} to render {Path.GetFileName(render.SourcePath)}: {e.Message}", e);

    /// <summary>A pass's frames written over the frames the sticker has, as the share of the pass done.</summary>
    private sealed class FrameProgress(IProgress<PassProgress> progress, int pass, int passes, int frames) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) =>
            Report(value.IsEnd ? 1 : Math.Clamp((double)value.Frame / Math.Max(1, frames), 0, 1));

        public void Report(double fraction) => progress.Report(new PassProgress(pass, passes, fraction));
    }
}
