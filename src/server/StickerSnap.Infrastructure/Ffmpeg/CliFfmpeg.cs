namespace StickerSnap.Infrastructure.Ffmpeg;

internal sealed class CliFfmpeg(IFfmpegLocator locator) : IFfmpeg
{
    public async Task<FfmpegResult> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = (await locator.FindAsync(cancellationToken)).Require();

        // -nostdin: ffmpeg would otherwise read keys from a console it shares with the server
        List<string> all = ["-hide_banner", "-nostdin"];
        if (progress is not null) all.AddRange(["-progress", "pipe:1", "-nostats"]);
        all.AddRange(arguments);

        var parser = progress is null ? null : new FfmpegProgressParser(progress);
        return await FfmpegProcess.RunAsync(
            Path.Combine(directory, FfmpegProcess.Ffmpeg), all, parser is null ? null : parser.Add, cancellationToken);
    }
}
