using System.Text.RegularExpressions;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// The version FFmpeg names in the first line of `ffmpeg -version`. Releases say `9.0.2` or
/// `n9.0.2-17-g2a571b6068`; builds from the master branch say `N-121234-g5f8a1c2b3d` and carry no
/// number, so for them the libavcodec line below tells the age: `N-55702-g920046a` is from 2013.
/// </summary>
/// <param name="Major">The release's major version; null for a build from master</param>
/// <param name="Libavcodec">The major version of the libavcodec the build carries</param>
internal sealed partial record FfmpegVersion(string Text, int? Major, int? Libavcodec = null)
{
    /// <summary>9.0 decodes animated WEBP; older builds cannot open such a source.</summary>
    public const int MinimumMajor = 9;

    /// <summary>The libavcodec of 9.0: a build from master with an older one predates 9.0.</summary>
    public const int MinimumLibavcodec = 63;

    /// <summary>A release of 9.0 or newer, or a build from master with the libavcodec of 9.0 or newer.</summary>
    public bool IsSupported => Major is int major
        ? major >= MinimumMajor
        : Libavcodec is int libavcodec && libavcodec >= MinimumLibavcodec;

    /// <summary>The version from the output of `ffmpeg -version`, or null when it is not FFmpeg's.</summary>
    public static FfmpegVersion? Parse(string output)
    {
        var firstLine = output.TrimStart().Split('\n', 2)[0];
        var match = FirstLine().Match(firstLine);
        if (!match.Success) return null;

        var text = match.Groups["version"].Value;
        if (text.StartsWith('n') && text.Length > 1 && char.IsAsciiDigit(text[1])) text = text[1..];
        var release = Release().Match(text);
        var libavcodec = LibavcodecLine().Match(output);
        return new FfmpegVersion(
            text,
            release.Success ? int.Parse(release.Groups["major"].Value) : null,
            libavcodec.Success ? int.Parse(libavcodec.Groups["major"].Value) : null);
    }

    [GeneratedRegex(@"^ffmpeg version (?<version>\S+)")]
    private static partial Regex FirstLine();

    [GeneratedRegex(@"^(?<major>\d+)\.\d+")]
    private static partial Regex Release();

    [GeneratedRegex(@"^\s*libavcodec\s+(?<major>\d+)\.", RegexOptions.Multiline)]
    private static partial Regex LibavcodecLine();
}
