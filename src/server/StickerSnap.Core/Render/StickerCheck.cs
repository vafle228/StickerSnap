using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>What Telegram would reject in a rendered sticker, read back from the file itself.</summary>
public static class StickerCheck
{
    /// <summary>A frame at 30 fps: a container's duration rounds up to it.</summary>
    private const double DurationSlackSec = 1.0 / 30;

    /// <summary>The broken rules, in English for the report; empty when the file passes.</summary>
    public static IReadOnlyList<string> Problems(MediaInfo sticker, PresetLimits limits)
    {
        List<string> problems = [];
        if (sticker.SizeBytes > limits.MaxBytes) problems.Add($"{sticker.SizeBytes} B is over {limits.MaxBytes} B");
        if (sticker.Format != MediaFormat.Webm || sticker.Codec != "vp9") problems.Add($"{sticker.Codec} in {sticker.Format}, not VP9 in WEBM");
        if (Math.Max(sticker.Width, sticker.Height) != limits.Side || sticker.Width > limits.Side || sticker.Height > limits.Side)
            problems.Add($"{sticker.Width}x{sticker.Height} has no side of exactly {limits.Side} px");
        if (sticker.DurationSec > limits.MaxDurationSec + DurationSlackSec)
            problems.Add($"{sticker.DurationSec:0.###} s is over {limits.MaxDurationSec} s");
        if (sticker.Fps.IsValid && sticker.Fps.ToDouble() > limits.MaxFps + 0.01)
            problems.Add($"{sticker.Fps} fps is over {limits.MaxFps}");
        if (sticker.HasAudio) problems.Add("has a sound track");
        return problems;
    }
}
