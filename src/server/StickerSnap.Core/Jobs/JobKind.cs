namespace StickerSnap.Core.Jobs;

/// <summary>What a background job does (contract, JobKind).</summary>
public enum JobKind
{
    /// <summary>«Точное превью»: a draft of the segment being edited, not yet saved</summary>
    Preview,

    /// <summary>«Экспорт WEBM»: final files of saved segments</summary>
    Export,

    /// <summary>Stickers sent to a Telegram pack</summary>
    Publish,
}

/// <summary>Where a job is in its life (contract, Job.state); see <see cref="Job"/> for the moves between them.</summary>
public enum JobState
{
    Queued,
    Running,

    /// <summary>Every segment done</summary>
    Succeeded,

    /// <summary>Some segments done, some failed: «Отправлено 2 из 3»</summary>
    Partial,

    Failed,
    Canceled,
}

/// <summary>Where one segment of a job is (contract, JobItem.state).</summary>
public enum JobItemState
{
    Queued,
    Exporting,
    Validating,
    Sending,
    Done,
    Failed,

    /// <summary>Left undone: the job was canceled or stopped before it came to this segment</summary>
    Skipped,
}

/// <summary>What a running job is busy with (contract, Job.progress.stage).</summary>
public enum JobStage
{
    /// <summary>Trying the steps of the fit ladder</summary>
    Probe,

    /// <summary>The first of two passes</summary>
    Pass1,

    /// <summary>The pass that writes the file</summary>
    Pass2,

    /// <summary>Checking the file against Telegram's rules</summary>
    Validate,

    /// <summary>Sending to Telegram</summary>
    Send,

    /// <summary>Telegram asked to wait (429): the job goes on by itself at <see cref="JobProgress.RetryAt"/></summary>
    Wait,
}
