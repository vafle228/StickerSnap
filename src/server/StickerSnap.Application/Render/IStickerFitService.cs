using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Render;

/// <summary>Renders a sticker under its preset's size limit (R7).</summary>
public interface IStickerFitService
{
    /// <param name="progress">Gets the estimates as `probe`, then the passes of each render</param>
    /// <exception cref="AppException">
    /// `cannot_fit` when no two-pass render came under the limit; the engine's errors as
    /// <see cref="IStickerRenderer.RenderAsync"/> has them
    /// </exception>
    Task<FittedSticker> FitAsync(StickerFit fit, IProgress<RenderProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// The preview of <see cref="FitAsync"/>: the same steps estimated from drafts, each kept at
    /// the output path, so the draft of the step the final render would take is left there. No
    /// step fitting leaves the last one, which the final render squeezes in with its two passes.
    /// </summary>
    /// <param name="progress">Gets each draft as `pass2`: a draft is the one pass the user waits for</param>
    /// <exception cref="AppException">The engine's errors as <see cref="IStickerRenderer.RenderAsync"/> has them</exception>
    Task<FittedSticker> DraftAsync(StickerFit fit, IProgress<RenderProgress>? progress, CancellationToken cancellationToken);
}
