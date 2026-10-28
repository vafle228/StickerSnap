using System.ComponentModel;
using System.Text;
using CliWrap;
using Microsoft.Extensions.Logging;
using StickerSnap.Application.Links;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Infrastructure.Links;

/// <summary>
/// YouTube through yt-dlp. The probe is `yt-dlp -J` with the format picked as the contract asks:
/// video only, the largest up to 720p by the shorter side, VP9 before H.264 and AV1 (what the
/// editor's engine reads best), a plain HTTPS file before a stream of pieces. The download takes
/// that very format by its id, so the size told is the size that comes.
/// </summary>
internal sealed class YtDlpVideoSite(
    IYtDlpLocator locator,
    IFfmpegLocator ffmpeg,
    ILogger<YtDlpVideoSite> logger) : IVideoSite
{
    /// <summary>Enough of stderr to tell why it failed.</summary>
    internal const int LogLines = 30;

    internal static readonly string[] FormatArguments = ["-f", "bv/b", "-S", "proto:https,res:720,vcodec:vp9"];

    /// <summary>No user's config file, one video, no colours, UTF-8 whatever the system's code page is.</summary>
    private static readonly string[] CommonArguments =
        ["--ignore-config", "--no-playlist", "--color", "never", "--encoding", "utf-8"];

    public LinkSite Site => LinkSite.Youtube;

    public async Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var result = await RunAsync(
            [.. CommonArguments, "-J", .. FormatArguments, "--", link.Url],
            line => output.AppendLine(line),
            link.Site,
            cancellationToken);
        if (result.ExitCode != 0) throw YtDlpErrors.Of(result.Log, link.Site, link.Url, result.Command);
        return YtDlpOutput.Parse(output.ToString(), link);
    }

    public async Task<long> DownloadAsync(
        LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        List<string> arguments =
        [
            .. CommonArguments,
            "-f", video.Variant,
            // An output template: a % in the path is a literal one
            "-o", path.Replace("%", "%%"),
            "--newline", "--progress-template", YtDlpOutput.ProgressTemplate,
            "--no-mtime",
        ];
        // For the rare format yt-dlp has to fix up after the download
        if ((await ffmpeg.FindAsync(cancellationToken)) is { Status: MediaEngineStatus.Ok, Directory: { } directory })
            arguments.AddRange(["--ffmpeg-location", directory]);
        arguments.AddRange(["--", video.Url]);

        var result = await RunAsync(
            arguments,
            line =>
            {
                if (YtDlpOutput.Progress(line) is { } step) progress(step);
            },
            video.Site,
            cancellationToken);
        if (result.ExitCode != 0) throw YtDlpErrors.Of(result.Log, video.Site, video.Url, result.Command);
        if (!File.Exists(path))
            throw YtDlpErrors.Of(
                $"ERROR: yt-dlp ended well but wrote no {Path.GetFileName(path)}\n{result.Log}", video.Site, video.Url, result.Command);
        return new FileInfo(path).Length;
    }

    private async Task<Result> RunAsync(
        IReadOnlyList<string> arguments, Action<string> onOutputLine, LinkSite site, CancellationToken cancellationToken)
    {
        var executable = locator.Find();
        var log = new FfmpegProcess.LogTail(LogLines);
        var command = FfmpegProcess.CommandLine(executable, arguments);
        try
        {
            // CliWrap kills the whole process tree when the token is cancelled
            var result = await Cli.Wrap(executable)
                .WithArguments(arguments)
                .WithEnvironmentVariables(new Dictionary<string, string?> { ["PYTHONIOENCODING"] = "utf-8" })
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(onOutputLine, Encoding.UTF8))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(log.Add, Encoding.UTF8))
                .ExecuteAsync(cancellationToken);
            if (result.ExitCode != 0) logger.LogWarning("{Command} failed with {Code}:\n{Log}", command, result.ExitCode, log);
            return new Result(result.ExitCode, log.ToString(), command);
        }
        catch (Win32Exception exception)
        {
            throw new AppException(ErrorCode.SiteChanged, $"{executable} would not start: {exception.Message}", exception)
            {
                Params = new Dictionary<string, object> { ["site"] = site.ToString().ToLowerInvariant() },
                Log = exception.Message,
            };
        }
    }

    private sealed record Result(int ExitCode, string Log, string Command);
}
