using System.Globalization;
using System.Numerics;
using System.Text.Json;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// Turns the JSON of `ffprobe -print_format json -show_format -show_streams -count_packets` into
/// <see cref="MediaInfo"/>. The format comes from the container and the codec, never the file name.
/// </summary>
internal static class FfprobeOutput
{
    public static readonly string[] Arguments =
        ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-count_packets"];

    /// <summary>Each frame's time and length in the first video stream, for <see cref="WithFrameDelays"/>.</summary>
    public static readonly string[] FrameDelayArguments =
        ["-v", "error", "-print_format", "json", "-select_streams", "v:0", "-show_entries", "stream=time_base:packet=pts,duration"];

    private static readonly HashSet<string> WebmCodecs = ["vp8", "vp9", "av1"];

    /// <param name="extension">The file's, lower case without the dot: what `format_unsupported` names</param>
    public static MediaInfo Parse(string json, string extension)
    {
        using (var document = Json(json))
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("format", out var format) || !root.TryGetProperty("streams", out var streams))
                throw Unreadable("ffprobe found neither a container nor streams");

            var video = streams.EnumerateArray().FirstOrDefault(IsPicture);
            if (video.ValueKind != JsonValueKind.Object) throw Unreadable("The file has no video stream");

            var codec = String(video, "codec_name") ?? "";
            var mediaFormat = FormatOf(String(format, "format_name") ?? "", codec, format)
                ?? throw new AppException(
                    ErrorCode.FormatUnsupported,
                    $"Container {String(format, "format_name")} with {codec} is not one of mp4, mov, webm, gif, webp")
                {
                    Params = new Dictionary<string, object> { ["ext"] = extension },
                };

            var (width, height) = DisplaySize(video);
            var baseRate = Rational.Parse(String(video, "r_frame_rate"));
            var averageRate = Rational.Parse(String(video, "avg_frame_rate"));
            var fps = averageRate is { IsValid: true } average ? average
                : baseRate is { IsValid: true } rate ? rate
                : throw Unreadable("ffprobe knows no frame rate of the video");
            var frameCount = Integer(video, "nb_frames") ?? Integer(video, "nb_read_packets");
            var duration = Number(format, "duration") ?? Number(video, "duration") ?? TagDuration(video);
            // Animated WEBP has no duration in the container; the probe reads its delays (WithFrameDelays)
            frameCount ??= duration is { } seconds ? (int)Math.Round(seconds * fps.ToDouble()) : null;
            if (frameCount is not > 0) throw Unreadable("ffprobe counted no frames in the video");
            duration ??= frameCount.Value / fps.ToDouble();

            return new MediaInfo(
                mediaFormat,
                codec == "webp_anim" ? "webp" : codec,
                width,
                height,
                fps,
                IsVariableFrameRate: baseRate is { IsValid: true } b && averageRate is { IsValid: true } a && !SameRate(b, a),
                frameCount.Value,
                duration.Value,
                HasAlpha(video),
                HasAudio: streams.EnumerateArray().Any(s => String(s, "codec_type") == "audio"),
                SizeBytes: Integer64(format, "size") ?? 0);
        }
    }

    /// <summary>
    /// Animated WEBP has no length in the container, only a delay per frame, and ffprobe's average
    /// rate is a guess from the first frames: with delays of 40, 200 and 1000 ms the guess makes a
    /// 2,1-second file 2,44 seconds long. The length is where the last frame ends. A frame rate in
    /// whole milliseconds jitters by one tick (30 fps is 33, 33, 34 ms), which is still a constant
    /// rate: the stream's own rate stays. Truly uneven delays give the average over the whole file.
    /// </summary>
    /// <param name="json">What ffprobe printed for <see cref="FrameDelayArguments"/></param>
    public static MediaInfo WithFrameDelays(MediaInfo media, string json)
    {
        using var document = Json(json);
        var root = document.RootElement;
        var timeBase = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray().Select(s => Rational.Parse(String(s, "time_base"))).FirstOrDefault()
            : null;
        if (timeBase is not { IsValid: true } tick || !root.TryGetProperty("packets", out var packets)) return media;

        var frames = packets.EnumerateArray()
            .Select(p => (Pts: Integer64(p, "pts"), Duration: Integer64(p, "duration")))
            .Where(p => p is { Pts: not null, Duration: not null })
            .Select(p => (Pts: p.Pts!.Value, Duration: p.Duration!.Value))
            .ToList();
        if (frames.Count == 0) return media;

        var ticks = frames.Max(f => f.Pts + f.Duration) - frames.Min(f => f.Pts);
        if (ticks <= 0) return media;
        var isVariable = frames.Max(f => f.Duration) - frames.Min(f => f.Duration) > 1;
        return media with
        {
            FrameCount = frames.Count,
            DurationSec = (double)ticks * tick.Num / tick.Den,
            IsVariableFrameRate = isVariable,
            Fps = isVariable ? Reduced((long)frames.Count * tick.Den, ticks * tick.Num) : media.Fps,
        };
    }

    /// <summary>
    /// WEBM declares its length in the header (Segment › Info › Duration), which is what ffprobe
    /// gives as the container's duration and what Telegram reads, while a player plays every frame.
    /// A long sticker (R19) declares 2.9 s over 20 s of frames, so the length is taken where the last
    /// frame ends and the header's stays as the declared one. A frame without a length of its own
    /// (no DefaultDuration in the track) lasts one frame of the stream's rate.
    /// </summary>
    /// <param name="json">What ffprobe printed for <see cref="FrameDelayArguments"/></param>
    public static MediaInfo WithFrameTimes(MediaInfo media, string json)
    {
        var declared = media with { DeclaredDurationSec = media.DurationSec };
        using var document = Json(json);
        var root = document.RootElement;
        var timeBase = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray().Select(s => Rational.Parse(String(s, "time_base"))).FirstOrDefault()
            : null;
        if (timeBase is not { IsValid: true } tick || !root.TryGetProperty("packets", out var packets)) return declared;

        var frameTicks = (long)Math.Round(tick.Den / (media.Fps.ToDouble() * tick.Num));
        var frames = packets.EnumerateArray()
            .Select(p => (Pts: Integer64(p, "pts"), Duration: Integer64(p, "duration")))
            .Where(p => p.Pts is not null)
            .Select(p => (Start: p.Pts!.Value, End: p.Pts!.Value + (p.Duration is > 0 ? p.Duration.Value : frameTicks)))
            .ToList();
        if (frames.Count == 0) return declared;

        var ticks = frames.Max(f => f.End) - frames.Min(f => f.Start);
        return ticks > 0 ? declared with { DurationSec = (double)ticks * tick.Num / tick.Den } : declared;
    }

    /// <summary>A video stream that is not cover art, which MP4 and M4A carry as `attached_pic`.</summary>
    private static bool IsPicture(JsonElement stream) =>
        String(stream, "codec_type") == "video"
        && !(stream.TryGetProperty("disposition", out var disposition)
             && disposition.TryGetProperty("attached_pic", out var cover)
             && cover.ValueKind == JsonValueKind.Number && cover.GetInt32() == 1);

    private static MediaFormat? FormatOf(string formatName, string codec, JsonElement format)
    {
        var names = formatName.Split(',');
        if (names.Contains("gif")) return MediaFormat.Gif;
        if (names.Any(n => n.StartsWith("webp", StringComparison.Ordinal))) return MediaFormat.Webp;
        if (names.Contains("mov") || names.Contains("mp4"))
            // QuickTime files carry the brand `qt  `; MP4 ones isom, mp42, avc1 and the like
            return Tag(format, "major_brand")?.Trim() == "qt" ? MediaFormat.Mov : MediaFormat.Mp4;
        if (names.Contains("webm") && WebmCodecs.Contains(codec)) return MediaFormat.Webm;
        return null;
    }

    /// <summary>The size the picture is shown at: pixel aspect ratio stretches it, a quarter turn swaps it.</summary>
    private static (int Width, int Height) DisplaySize(JsonElement video)
    {
        var width = Integer(video, "width") ?? 0;
        var height = Integer(video, "height") ?? 0;
        if (width <= 0 || height <= 0) throw Unreadable("ffprobe knows no size of the video");

        if (Rational.Parse(String(video, "sample_aspect_ratio")) is { IsValid: true } sar && sar.Num != sar.Den)
            width = (int)Math.Round(width * sar.ToDouble());

        var rotation = video.TryGetProperty("side_data_list", out var sideData)
            ? sideData.EnumerateArray()
                .Select(d => d.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : 0)
                .FirstOrDefault(r => r != 0)
            : 0;
        return Math.Abs(Math.Round(rotation)) % 180 == 90 ? (height, width) : (width, height);
    }

    /// <summary>
    /// VP8 and VP9 in WEBM say so in the `alpha_mode` tag while pix_fmt stays yuv420p. GIF and WEBP
    /// always decode to a format with alpha, so for them it means only «may be transparent».
    /// </summary>
    private static bool HasAlpha(JsonElement video)
    {
        if (Tag(video, "alpha_mode") == "1") return true;
        var pixelFormat = String(video, "pix_fmt") ?? "";
        return pixelFormat.StartsWith("yuva", StringComparison.Ordinal)
            || pixelFormat.StartsWith("gbrap", StringComparison.Ordinal)
            || pixelFormat.StartsWith("ya", StringComparison.Ordinal)
            || pixelFormat is "pal8"
            || pixelFormat.Contains("rgba") || pixelFormat.Contains("bgra")
            || pixelFormat.Contains("argb") || pixelFormat.Contains("abgr");
    }

    /// <summary>Equal fractions; 30/1 and 60/2 are the same rate.</summary>
    private static bool SameRate(Rational a, Rational b) => (long)a.Num * b.Den == (long)b.Num * a.Den;

    /// <summary>Matroska writes a stream's length only in its DURATION tag: `00:00:01.000000000`.</summary>
    private static double? TagDuration(JsonElement stream) =>
        Tag(stream, "DURATION") is { } text
        && TimeSpan.TryParse(text.Length > 16 ? text[..16] : text, CultureInfo.InvariantCulture, out var duration)
            ? duration.TotalSeconds
            : null;

    private static string? Tag(JsonElement element, string name)
    {
        if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) return null;
        // Tag names keep the case the muxer wrote: alpha_mode or ALPHA_MODE
        foreach (var tag in tags.EnumerateObject())
            if (string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))
                return tag.Value.GetString();
        return null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static int? Integer(JsonElement element, string name) =>
        int.TryParse(String(element, name), CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? Integer64(JsonElement element, string name) =>
        long.TryParse(String(element, name), CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Number(JsonElement element, string name) =>
        double.TryParse(String(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    private static JsonDocument Json(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw Unreadable($"ffprobe printed no JSON: {e.Message}");
        }
    }

    private static Rational Reduced(long num, long den)
    {
        var divisor = (long)BigInteger.GreatestCommonDivisor(num, den);
        return new Rational((int)(num / divisor), (int)(den / divisor));
    }

    private static AppException Unreadable(string message) => new(ErrorCode.SourceUnreadable, message) { Log = message };
}
