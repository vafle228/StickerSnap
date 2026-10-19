namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Runs the FFmpeg that <see cref="IFfmpegLocator"/> found.</summary>
internal interface IFfmpeg
{
    /// <summary>
    /// Runs ffmpeg with <paramref name="arguments"/>, each one a whole argument, never a command
    /// line to split. Cancelling kills the process tree and throws <see cref="OperationCanceledException"/>.
    /// A failing run is returned, not thrown: what it means is up to the caller.
    /// </summary>
    /// <param name="progress">
    /// Gets a report on each `-progress` block; when set, ffmpeg writes progress to its output,
    /// and <see cref="FfmpegResult.Output"/> stays empty
    /// </param>
    /// <exception cref="Core.Errors.AppException">`ffmpeg_missing` when there is no usable FFmpeg</exception>
    Task<FfmpegResult> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>How a run of ffmpeg ended.</summary>
/// <param name="Output">What it wrote to its standard output, such as `metadata=print:file=-`</param>
/// <param name="Log">The last lines of its standard error: what goes under «Подробности»</param>
/// <param name="Command">The command line it ran, for a bug's report</param>
internal sealed record FfmpegResult(int ExitCode, string Output, string Log, string? Command = null)
{
    public bool IsSuccess => ExitCode == 0;

    /// <summary>The context of an error this run ends with: its command.</summary>
    public IReadOnlyDictionary<string, string> Context =>
        Command is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["command"] = Command };
}

/// <summary>One block of `-progress pipe:1`.</summary>
/// <param name="Frame">Frames written so far</param>
/// <param name="OutTime">Time of the output written so far; null while ffmpeg says N/A</param>
/// <param name="Speed">Times real time; null while ffmpeg says N/A</param>
/// <param name="IsEnd">The last block, `progress=end`</param>
internal sealed record FfmpegProgress(long Frame, TimeSpan? OutTime, double? Speed, bool IsEnd);
