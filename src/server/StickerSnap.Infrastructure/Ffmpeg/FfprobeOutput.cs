using System.Globalization;
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

    private static readonly HashSet<string> WebmCodecs = ["vp8", "vp9", "av1"];

    /// <param name="extension">The file's, lower case without the dot: what `format_unsupported` names</param>
    public static MediaInfo Parse(string json, string extension)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw Unreadable($"ffprobe printed no JSON: {e.Message}");
        }

        using (document)
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
            // Animated WEBP has no duration in the container and frames that are all whole
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

    private static AppException Unreadable(string message) => new(ErrorCode.SourceUnreadable, message) { Log = message };
}
