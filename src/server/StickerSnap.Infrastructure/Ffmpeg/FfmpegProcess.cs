using System.ComponentModel;
using System.Text;
using CliWrap;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>Runs ffmpeg or ffprobe from the directory the locator found.</summary>
internal static class FfmpegProcess
{
    public static readonly string Ffmpeg = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    public static readonly string Ffprobe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    /// <summary>Enough of stderr to show why a run failed; a render's whole log runs to megabytes.</summary>
    internal const int LogLines = 40;

    /// <param name="onOutputLine">Takes standard output line by line; null collects it into the result</param>
    public static async Task<FfmpegResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var log = new LogTail(LogLines);
        var command = Cli.Wrap(executable)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(onOutputLine is null ? PipeTarget.ToStringBuilder(output) : PipeTarget.ToDelegate(onOutputLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(log.Add));
        try
        {
            // CliWrap kills the whole process tree when the token is cancelled
            var result = await command.ExecuteAsync(cancellationToken);
            return new FfmpegResult(result.ExitCode, output.ToString(), log.ToString());
        }
        catch (Win32Exception e)
        {
            throw new AppException(ErrorCode.FfmpegMissing, $"{executable} would not start: {e.Message}", e);
        }
    }

    /// <summary>The last lines written, oldest first.</summary>
    internal sealed class LogTail(int capacity)
    {
        private readonly Queue<string> _lines = new(capacity);

        public void Add(string line)
        {
            lock (_lines)
            {
                if (_lines.Count == capacity) _lines.Dequeue();
                _lines.Enqueue(line);
            }
        }

        public override string ToString()
        {
            lock (_lines) return string.Join('\n', _lines);
        }
    }
}
