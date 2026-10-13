using StickerSnap.Core.Projects;

namespace StickerSnap.Core.Jobs;

/// <summary>One segment of a job (contract, JobItem): «Экспорт 2 из 3».</summary>
/// <param name="Fraction">0..1 of this segment</param>
/// <param name="Preview">A preview's draft, once it is done</param>
/// <param name="Export">An export's sticker, once it is done</param>
public sealed record JobItem(
    string SegmentId,
    JobItemState State,
    double Fraction,
    JobError? Error = null,
    PreviewFile? Preview = null,
    SegmentExport? Export = null)
{
    /// <summary>Done, failed or skipped: the segment will not move again in this run.</summary>
    public bool IsOver => State is JobItemState.Done or JobItemState.Failed or JobItemState.Skipped;
}

/// <summary>«Точное · 186 КБ»: the draft of the exact preview.</summary>
/// <param name="FileId">How GET /api/v1/files knows the draft</param>
/// <param name="Bytes">What the final sticker is expected to weigh: the draft itself is larger</param>
public sealed record PreviewFile(string FileId, long Bytes);
