using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Renders stickers with libvpx-vp9; two passes keep their statistics in a temp directory.</summary>
internal sealed class FfmpegStickerRenderer(IFfmpeg ffmpeg) : IStickerRenderer
{
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
