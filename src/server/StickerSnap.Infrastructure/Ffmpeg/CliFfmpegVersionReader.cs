using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;

namespace StickerSnap.Infrastructure.Ffmpeg;

internal sealed class CliFfmpegVersionReader : IFfmpegVersionReader
{
    /// <summary>`-version` answers at once; a binary that hangs is not a usable FFmpeg.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> ReadAsync(string executable, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var result = await Cli.Wrap(executable)
                .WithArguments("-version")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(timeout.Token);
            return result.IsSuccess ? result.StandardOutput : null;
        }
        catch (Exception e) when (e is Win32Exception or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
