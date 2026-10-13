using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Estimates;

/// <summary>POST /projects/{id}/estimate: what a segment as the editor has it will weigh (3a «Экспорт», 3c G).</summary>
public interface IEstimateService
{
    /// <summary>
    /// The forecast of <paramref name="segment"/>, saved or not, cut from the project's source:
    /// measured the way the export measures its ladder, so the inspector and the export agree.
    /// The same segment of the same source is answered from memory.
    /// </summary>
    /// <exception cref="AppException">
    /// bad_request with the broken fields under `segment.`; project_not_found, project_invalid,
    /// project_too_new; source_not_found when the video is gone; render_failed, disk_full and
    /// ffmpeg_missing from the media engine
    /// </exception>
    Task<SegmentEstimate> EstimateAsync(Guid projectId, Segment segment, CancellationToken cancellationToken);
}

/// <summary>The forecast of a segment (contract, Estimate).</summary>
/// <param name="Bytes">Of the final file at the first step of the ladder: nothing given up</param>
/// <param name="LimitBytes">The preset's limit</param>
/// <param name="Fits">
/// <paramref name="Bytes"/> is within <see cref="SizeForecast.Allowance"/>: the export gives nothing
/// up, or a file rendered as it is passes the limit
/// </param>
/// <param name="Output">The sticker at the first step: size, rate, how long the loop plays</param>
/// <param name="LikelyConcessions">
/// What the export will likely give up: the concessions of the first step whose estimate fits, or
/// of the last step when none does; empty when the sticker fits or fitting is off
/// </param>
/// <param name="Suggestions">Edits that bring the sticker within the allowance; empty when it fits</param>
public sealed record SegmentEstimate(
    long Bytes,
    long LimitBytes,
    bool Fits,
    StickerOutput Output,
    IReadOnlyList<Concession> LikelyConcessions,
    IReadOnlyList<SizeSuggestion> Suggestions);
