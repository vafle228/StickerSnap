using System.Globalization;
using StickerSnap.Application.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// The ffmpeg arguments that render a sticker as VP9 in WEBM, one pass at a time: the frames and
/// the picture of <see cref="StickerFilterGraph"/>, then the encoder.
/// </summary>
internal static class Vp9StickerArguments
{
    /// <summary>
    /// The arguments of one run: the only one for <see cref="RateControl.ConstantQuality"/>, pass
    /// 1 (analysis, no file) or 2 for <see cref="RateControl.TwoPass"/>.
    /// </summary>
    /// <param name="pass">0 for a one-pass render, else 1 or 2</param>
    /// <param name="passLog">The prefix of the two passes' statistics file</param>
    public static IReadOnlyList<string> For(StickerRender render, int pass, string? passLog)
    {
        var spec = render.Spec;
        List<string> arguments = [.. StickerFilterGraph.Input(render)];
        arguments.AddRange(["-filter_complex", StickerFilterGraph.Filters(render), "-map", StickerFilterGraph.Output, "-an", "-sn", "-dn"]);
        arguments.AddRange(["-c:v", "libvpx-vp9", "-pix_fmt", spec.KeepAlpha ? "yuva420p" : "yuv420p"]);
        arguments.AddRange(Rate(render.Rate, pass, passLog));
        arguments.AddRange(Effort(render.Effort));

        if (pass == 1)
            arguments.AddRange(["-f", "null", "-"]);
        else
            arguments.AddRange(["-f", "webm", "-y", render.OutputPath]);
        return arguments;
    }

    private static IEnumerable<string> Rate(RateControl rate, int pass, string? passLog) => rate switch
    {
        RateControl.ConstantQuality q => ["-crf", Number(q.Crf), "-b:v", "0"],
        RateControl.TwoPass t when pass is 1 or 2 && passLog is not null =>
            ["-b:v", Number(t.BitsPerSecond), "-pass", Number(pass), "-passlogfile", passLog],
        RateControl.TwoPass => throw new ArgumentException("A two-pass render runs as pass 1 and pass 2 with a pass log"),
        _ => throw new ArgumentOutOfRangeException(nameof(rate), rate, null),
    };

    /// <summary>
    /// A draft is for the eye: realtime and the fastest speed. The final file takes good quality
    /// at speed 2, a common choice for short clips; row-mt spreads both over the cores.
    /// </summary>
    private static IEnumerable<string> Effort(RenderEffort effort) => effort switch
    {
        RenderEffort.Draft => ["-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1"],
        RenderEffort.Final => ["-deadline", "good", "-cpu-used", "2", "-row-mt", "1"],
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
    };

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
