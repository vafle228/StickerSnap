namespace StickerSnap.Core.Jobs;

/// <summary>One segment of a job (contract, JobItem): «Экспорт 2 из 3».</summary>
/// <param name="Fraction">0..1 of this segment</param>
public sealed record JobItem(string SegmentId, JobItemState State, double Fraction, JobError? Error = null)
{
    /// <summary>Done, failed or skipped: the segment will not move again in this run.</summary>
    public bool IsOver => State is JobItemState.Done or JobItemState.Failed or JobItemState.Skipped;
}
