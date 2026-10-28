using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Application.Links;

/// <summary>POST /api/v1/links/probe (artboard 20·3·4, «Узнаю о видео…»): what the video by a link is.</summary>
public interface ILinkService
{
    /// <summary>
    /// The video of a pasted link, without downloading it. The answer is kept for a while, so the
    /// download that follows a probe does not ask the site again. Cancelling is «Отменить».
    /// </summary>
    /// <exception cref="AppException">
    /// what <see cref="VideoLink.Parse"/> and <see cref="IVideoSites.ProbeAsync"/> throw
    /// </exception>
    Task<LinkVideo> ProbeAsync(string url, CancellationToken cancellationToken);
}
