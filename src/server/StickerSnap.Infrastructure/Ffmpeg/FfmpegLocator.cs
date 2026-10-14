using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Media;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// Looks for FFmpeg in StickerSnap__FfmpegDir, then next to the server (where the installer puts
/// it), then on PATH, and takes the first one that answers `-version` with 9.0 or newer. An older
/// one does not hide a usable one further on: it is reported only when there is no usable one.
/// A usable FFmpeg is kept for the life of the process; a missing or old one is looked for again
/// on the next call, so installing or updating it does not need a restart.
/// </summary>
internal sealed class FfmpegLocator(
    IOptions<FfmpegOptions> options,
    IFfmpegVersionReader reader,
    ILogger<FfmpegLocator> logger) : IFfmpegLocator
{
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
            if (lookup.Status == MediaEngineStatus.Ok) _found = lookup;
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

    private Task<FfmpegLookup> SearchAsync(CancellationToken cancellationToken) =>
        SearchAsync(
            Directories(options.Value.FfmpegDir, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH")),
            cancellationToken);

    /// <summary>The first usable FFmpeg in <paramref name="directories"/>, else the first old one.</summary>
    internal async Task<FfmpegLookup> SearchAsync(IEnumerable<string> directories, CancellationToken cancellationToken)
    {
        FfmpegLookup? tooOld = null;
        foreach (var directory in directories)
        {
            var executable = Path.Combine(directory, FfmpegProcess.Ffmpeg);
            if (!File.Exists(executable)) continue;

            var output = await reader.ReadAsync(executable, cancellationToken);
            if (output is null || FfmpegVersion.Parse(output) is not { } version)
            {
                logger.LogWarning("{Executable} does not answer -version as FFmpeg does, skipping it", executable);
                continue;
            }

            if (!version.IsSupported)
            {
                logger.LogWarning("FFmpeg {Version} in {Directory} is older than {Minimum}.0, looking further", version.Text, directory, FfmpegVersion.MinimumMajor);
                tooOld ??= FfmpegLookup.Found(version, directory);
                continue;
            }

            logger.LogInformation("FFmpeg {Version} found in {Directory}", version.Text, directory);
            return FfmpegLookup.Found(version, directory);
        }

        if (tooOld is not null) return tooOld;
        logger.LogWarning("FFmpeg was not found: set StickerSnap__FfmpegDir or run npm run fetch:ffmpeg");
        return FfmpegLookup.Missing;
    }
}
