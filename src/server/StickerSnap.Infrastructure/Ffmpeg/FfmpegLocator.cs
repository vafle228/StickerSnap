using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Ffmpeg;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// Looks for FFmpeg in StickerSnap__FfmpegDir, then next to the server (where the installer puts
/// it), then on PATH, and takes the first one that answers `-version`. A found FFmpeg is kept for
/// the life of the process; a missing one is looked for again on the next call, so installing it
/// does not need a restart.
/// </summary>
internal sealed class FfmpegLocator(
    IOptions<FfmpegOptions> options,
    IFfmpegVersionReader reader,
    ILogger<FfmpegLocator> logger) : IFfmpegLocator
{
    private static readonly string Executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private FfmpegLookup? _found;

    public async Task<FfmpegLookup> FindAsync(CancellationToken cancellationToken)
    {
        if (_found is not null) return _found;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_found is not null) return _found;
            var lookup = await SearchAsync(cancellationToken);
            if (lookup.Status != FfmpegStatus.Missing) _found = lookup;
            return lookup;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The directories to look in, in order: the option, the server's own, then PATH.</summary>
    internal static IEnumerable<string> Directories(string? configured, string baseDirectory, string? path)
    {
        if (!string.IsNullOrWhiteSpace(configured)) yield return Path.GetFullPath(configured);
        yield return baseDirectory;
        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return directory;
    }

    private async Task<FfmpegLookup> SearchAsync(CancellationToken cancellationToken)
    {
        var directories = Directories(
            options.Value.FfmpegDir,
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("PATH"));

        foreach (var directory in directories)
        {
            var executable = Path.Combine(directory, Executable);
            if (!File.Exists(executable)) continue;

            var output = await reader.ReadAsync(executable, cancellationToken);
            if (output is null || FfmpegVersion.Parse(output) is not { } version)
            {
                logger.LogWarning("{Executable} does not answer -version as FFmpeg does, skipping it", executable);
                continue;
            }

            logger.LogInformation("FFmpeg {Version} found in {Directory}", version.Text, directory);
            return FfmpegLookup.Found(version, directory);
        }

        logger.LogWarning("FFmpeg was not found: set StickerSnap__FfmpegDir or run npm run fetch:ffmpeg");
        return FfmpegLookup.Missing;
    }
}
