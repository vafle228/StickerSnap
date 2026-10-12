using System.Globalization;
using StickerSnap.Application.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>The ffmpeg arguments that render a sticker as VP9 in WEBM, one pass at a time.</summary>
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
        List<string> arguments = [];

        // FFmpeg's own VP8 and VP9 decoders drop the alpha WEBM keeps beside the picture; libvpx reads it
        if (spec.KeepAlpha && render.Source.Codec is "vp9" or "vp8")
            arguments.AddRange(["-c:v", render.Source.Codec == "vp9" ? "libvpx-vp9" : "libvpx"]);
        if (spec.StartSec > 0) arguments.AddRange(["-ss", Number(spec.StartSec)]);
        arguments.AddRange(["-t", Number(spec.DurationSec), "-i", render.SourcePath]);

        arguments.AddRange(["-map", "0:v:0", "-an", "-sn", "-dn", "-vf", Filters(render)]);
        arguments.AddRange(["-c:v", "libvpx-vp9", "-pix_fmt", spec.KeepAlpha ? "yuva420p" : "yuv420p"]);
        arguments.AddRange(Rate(render.Rate, pass, passLog));
        arguments.AddRange(Effort(render.Effort));

        if (pass == 1)
            arguments.AddRange(["-f", "null", "-"]);
        else
            arguments.AddRange(["-f", "webm", "-y", render.OutputPath]);
        return arguments;
    }

    /// <summary>
    /// The picture is first brought to its size as shown with square pixels (MediaInfo's width
    /// and height already apply rotation and the pixel aspect ratio), then cropped, scaled (through
    /// the smaller detail size when the fit ladder gave detail up) and resampled to the sticker's rate.
    /// </summary>
    internal static string Filters(StickerRender render)
    {
        var spec = render.Spec;
        var crop = spec.Crop;
        List<string> filters =
        [
            $"scale={render.Source.Width}:{render.Source.Height}",
            "setsar=1",
            $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}",
        ];
        if (spec.Detail is { } detail) filters.Add($"scale={detail.Width}:{detail.Height}:flags=lanczos");
        filters.AddRange(
        [
            $"scale={spec.Width}:{spec.Height}:flags=lanczos",
            $"fps={spec.Fps}",
            $"format={(spec.KeepAlpha ? "yuva420p" : "yuv420p")}",
        ]);
        return string.Join(',', filters);
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
