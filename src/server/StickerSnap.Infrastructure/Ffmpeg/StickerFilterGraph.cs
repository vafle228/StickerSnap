using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// How ffmpeg reads a sticker's frames from its source and makes the sticker of them: the input
/// options, and the filtergraph from the source's video stream to `[v]`.
/// <para>
/// The frames are picked by their numbers, as the editor counts them: read from the first one as
/// <see cref="SourceInput"/> reads a source, then `trim` by frame and `select` for the excluded ones.
/// </para>
/// <para>
/// Then the speed (`setpts`) and the sticker's rate (`fps`), padded with the last frame and cut to
/// exactly the frames the sticker plays, so its length is the one the bitrate was worked out on.
/// The rate comes before the picture: no more than 90 frames are scaled whatever the speed. Then
/// the picture is brought to its size as shown with square pixels, cropped (`exact`, so an odd
/// corner stays where it is), shrunk to the detail of the fit ladder and scaled to the sticker,
/// which squeezes it for a stretch; its pixels are square again, so a player shows it squeezed.
/// A ping-pong plays the frames back without the two ends.
/// </para>
/// </summary>
internal static class StickerFilterGraph
{
    /// <summary>The label of the graph's output, for `-map`.</summary>
    public const string Output = "[v]";

    /// <summary>The input options and the source, up to and including `-i`.</summary>
    public static IReadOnlyList<string> Input(StickerRender render)
    {
        var frames = render.Spec.Clip.Frames;
        return SourceInput.Arguments(render.SourcePath, render.Source, render.Spec.KeepAlpha, frames.First, frames.Last);
    }

    /// <summary>The filtergraph for `-filter_complex`, from the first video stream to <see cref="Output"/>.</summary>
    public static string Filters(StickerRender render)
    {
        var spec = render.Spec;
        var source = render.Source;
        var frames = spec.Clip.Frames;
        var forward = spec.ForwardFrames;

        List<string> chain = [SourceInput.Trim(source, frames.First, frames.Last)];
        if (frames.Excluded.Count > 0)
        {
            // The frames left close up at the source's rate: a cut frame takes its time with it
            // `n` counts the frames trim lets through, from the first frame of the sticker
            var runs = frames.Runs().Select(run => run.First == run.Last
                ? $"eq(n,{run.First - frames.First})"
                : $"between(n,{run.First - frames.First},{run.Last - frames.First})");
            chain.Add($"select='{string.Join('+', runs)}'");
            chain.Add($"setpts=N/({SourceInput.Rate(source)}{Times(spec.Clip.Speed)})/TB");
        }
        else
        {
            chain.Add(spec.Clip.Speed == 1 ? "setpts=PTS-STARTPTS" : $"setpts=(PTS-STARTPTS)/{Number(spec.Clip.Speed)}");
        }

        chain.Add($"fps={spec.Fps}");
        chain.Add("tpad=stop=-1:stop_mode=clone");
        chain.Add($"trim=end_frame={forward}");

        chain.Add($"scale={source.Width}:{source.Height}");
        chain.Add("setsar=1");
        if (spec.Crop != new PixelRect(0, 0, source.Width, source.Height))
            chain.Add($"crop={spec.Crop.Width}:{spec.Crop.Height}:{spec.Crop.X}:{spec.Crop.Y}:exact=1");
        if (spec.Detail is { } detail) chain.Add($"scale={detail.Width}:{detail.Height}:flags=lanczos");
        chain.Add($"scale={spec.Width}:{spec.Height}:flags=lanczos");

        // scale keeps the picture's proportion as shown by stretching its pixels; a sticker's are square
        chain.Add("setsar=1");

        var format = $"format={(spec.KeepAlpha ? "yuva420p" : "yuv420p")}";
        var head = $"[0:v:0]{string.Join(',', chain)}";

        // 2 frames ping-pong into themselves: the way back without its ends is empty
        if (!spec.Clip.PingPong || forward < 3) return $"{head},{format}{Output}";
        return $"{head},split[forth][back];" +
               $"[back]reverse,trim=start_frame=1:end_frame={forward - 1},setpts=PTS-STARTPTS[backward];" +
               $"[forth][backward]concat=n=2:v=1:a=0,{format}{Output}";
    }

    private static string Times(double speed) => speed == 1 ? "" : $"*{Number(speed)}";

    private static string Number(double value) => SourceInput.Number(value);
}
