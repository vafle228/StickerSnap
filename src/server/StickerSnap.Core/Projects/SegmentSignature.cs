using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StickerSnap.Core.Projects;

/// <summary>
/// A mark of everything that makes a segment's sticker: its frames, its frame, speed, loop,
/// preset and fitting, and the source it is cut from. Two segments with the same mark give the
/// same file; its name is not in it, since renaming changes no frame.
/// </summary>
public static class SegmentSignature
{
    private const string Version = "v1";

    /// <returns>`v1:` and a hex hash; its form is the server's own</returns>
    public static string Of(Segment segment, ProjectSource source)
    {
        var canonical = string.Join(
            '|',
            source.Fingerprint,
            Number(segment.InFrame),
            Number(segment.OutFrame),
            string.Join(',', segment.ExcludedFrames.Select(Number)),
            FrameOf(segment.Frame),
            Number(segment.Speed),
            segment.Loop,
            segment.Preset,
            segment.LongSticker,
            segment.AutoFit);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"{Version}:{Convert.ToHexStringLower(hash.AsSpan(0, 16))}";
    }

    private static string FrameOf(SegmentFrame frame) => frame switch
    {
        SegmentFrame.Crop crop =>
            $"crop {Number(crop.Rect.X)} {Number(crop.Rect.Y)} {Number(crop.Rect.Width)} {Number(crop.Rect.Height)}",
        SegmentFrame.Stretch stretch => $"stretch {Number(stretch.Aspect.Width)} {Number(stretch.Aspect.Height)}",
        _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "A frame the signature does not know"),
    };

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
