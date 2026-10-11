using System.Text.RegularExpressions;

namespace StickerSnap.Core.Ffmpeg;

/// <summary>
/// The version FFmpeg names in the first line of `ffmpeg -version`. Releases say `9.0.2` or
/// `n9.0.2-17-g2a571b6068`; builds from the master branch say `N-121234-g5f8a1c2b3d` and carry no
/// number, but are newer than any release.
/// </summary>
public sealed partial record FfmpegVersion(string Text, int? Major)
{
    /// <summary>9.0 decodes animated WEBP; older builds cannot open such a source.</summary>
    public const int MinimumMajor = 9;

    /// <summary>A release of 9.0 or newer, or a build from master.</summary>
    public bool IsSupported => Major is not int major || major >= MinimumMajor;

    /// <summary>The version from the output of `ffmpeg -version`, or null when it is not FFmpeg's.</summary>
    public static FfmpegVersion? Parse(string output)
    {
        var firstLine = output.TrimStart().Split('\n', 2)[0];
        var match = FirstLine().Match(firstLine);
        if (!match.Success) return null;

        var text = match.Groups["version"].Value;
        if (text.StartsWith('n') && text.Length > 1 && char.IsAsciiDigit(text[1])) text = text[1..];
        var release = Release().Match(text);
        return new FfmpegVersion(text, release.Success ? int.Parse(release.Groups["major"].Value) : null);
    }

    [GeneratedRegex(@"^ffmpeg version (?<version>\S+)")]
    private static partial Regex FirstLine();

    [GeneratedRegex(@"^(?<major>\d+)\.\d+")]
    private static partial Regex Release();
}
