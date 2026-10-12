using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;

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

    public async Task<RenderedSticker> RenderAsync(StickerRender render, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(render.OutputPath))!);

        if (render.Rate is RateControl.TwoPass)
        {
            var passes = Directory.CreateTempSubdirectory("stickersnap-pass-");
            try
            {
                var log = Path.Combine(passes.FullName, "vp9");
                await RunAsync(Vp9StickerArguments.For(render, 1, log), "pass 1", render, cancellationToken);
                await RunAsync(Vp9StickerArguments.For(render, 2, log), "pass 2", render, cancellationToken);
            }
            finally
            {
                passes.Delete(recursive: true);
            }
        }
        else
        {
            await RunAsync(Vp9StickerArguments.For(render, 0, null), "the render", render, cancellationToken);
        }

        return new RenderedSticker(render.OutputPath, new FileInfo(render.OutputPath).Length);
    }

    /// <summary>A draft at the probe's CRF, a fraction of a second, scaled to the final render's size.</summary>
    public async Task<long> EstimateAsync(StickerProbe probe, CancellationToken cancellationToken)
    {
        var scratch = Directory.CreateTempSubdirectory("stickersnap-probe-");
        try
        {
            var draft = new StickerRender(
                probe.SourcePath,
                probe.Source,
                probe.Spec,
                new RateControl.ConstantQuality(probe.Crf),
                RenderEffort.Draft,
                Path.Combine(scratch.FullName, "probe.webm"));
            var rendered = await RenderAsync(draft, cancellationToken);
            return (long)Math.Round(rendered.SizeBytes / (probe.Spec.KeepAlpha ? TransparentDraftOverFinal : DraftOverFinal));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, string step, StickerRender render, CancellationToken cancellationToken)
    {
        var result = await ffmpeg.RunAsync(arguments, progress: null, cancellationToken);
        if (!result.IsSuccess)
            throw new AppException(
                ErrorCode.RenderFailed,
                $"ffmpeg failed {step} of {Path.GetFileName(render.SourcePath)} (exit code {result.ExitCode})")
            {
                Log = result.Log,
            };
    }
}
