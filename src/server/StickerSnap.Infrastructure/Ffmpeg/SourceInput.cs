using System.Globalization;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// How ffmpeg reads a source from a frame on, the frames counted as the editor counts them: the
/// sticker render and the editor's pictures read it the same way.
/// <para>
/// A video container with a steady rate is sought to half a frame before the first frame wanted,
/// so a frame from the end of a long video does not decode it all; ffmpeg drops what comes before
/// that time, so the first frame left is the one wanted. A GIF, a WEBP or a variable rate is read
/// from its first frame: there a time is not a frame.
/// </para>
/// </summary>
internal static class SourceInput
{
    /// <summary>
    /// The input options up to and including `-i`: the decoder, the seek to <paramref name="first"/>
    /// and, when <paramref name="last"/> is given, the end of the reading after that frame.
    /// </summary>
    /// <param name="keepAlpha">Whether the alpha a WEBM keeps beside the picture is read too</param>
    public static List<string> Arguments(string path, MediaInfo source, bool keepAlpha, int first, int? last)
    {
        List<string> arguments = [];

        // FFmpeg's own VP8 and VP9 decoders drop the alpha WEBM keeps beside the picture; libvpx reads it
        if (keepAlpha && source.Codec is "vp9" or "vp8")
            arguments.AddRange(["-c:v", source.Codec == "vp9" ? "libvpx-vp9" : "libvpx"]);

        if (IsSought(source))
        {
            var rate = SceneCompiler.SourceRate(source);
            var start = Math.Max(0, (first - 0.5) / rate);
            if (start > 0) arguments.AddRange(["-ss", Number(start)]);
            if (last is { } end) arguments.AddRange(["-t", Number((end + 0.5) / rate - start)]);
        }

        arguments.AddRange(["-i", path]);
        return arguments;
    }

    /// <summary>
    /// How many frames of <paramref name="source"/> the input of <see cref="Arguments"/> skipped:
    /// a `trim` by frame numbers counts from there.
    /// </summary>
    public static int Offset(MediaInfo source, int first) => IsSought(source) && first > 0 ? first : 0;

    /// <summary>`trim` to frames <paramref name="first"/> to <paramref name="last"/> of the source, both included.</summary>
    public static string Trim(MediaInfo source, int first, int last)
    {
        var offset = Offset(source, first);
        var start = first - offset;
        var end = last + 1 - offset;
        return start > 0 ? $"trim=start_frame={start}:end_frame={end}" : $"trim=end_frame={end}";
    }

    /// <summary>The source's rate as a filter takes it: `30000/1001`, `25`.</summary>
    public static string Rate(MediaInfo source)
    {
        var fps = source.Fps;
        return !fps.IsValid ? Number(SceneCompiler.SourceRate(source)) : fps.Den == 1 ? Number(fps.Num) : $"{fps.Num}/{fps.Den}";
    }

    public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>A container whose frames sit at their times, so seeking to a time finds a frame.</summary>
    private static bool IsSought(MediaInfo source) =>
        source.Format is MediaFormat.Mp4 or MediaFormat.Mov or MediaFormat.Webm && !source.IsVariableFrameRate && source.Fps.IsValid;
}
