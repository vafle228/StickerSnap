using System.Globalization;

namespace StickerSnap.Infrastructure.Ffmpeg;

/// <summary>
/// Reads the `key=value` lines of `-progress pipe:1`. A block ends with `progress=continue` or
/// `progress=end`, and each ended block is reported once.
/// </summary>
internal sealed class FfmpegProgressParser(IProgress<FfmpegProgress> progress)
{
    private long _frame;
    private TimeSpan? _outTime;
    private double? _speed;

    public void Add(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0) return;
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        switch (key)
        {
            case "frame" when long.TryParse(value, CultureInfo.InvariantCulture, out var frame):
                _frame = frame;
                break;
            case "out_time_us":
                _outTime = long.TryParse(value, CultureInfo.InvariantCulture, out var us) && us >= 0
                    ? TimeSpan.FromMicroseconds(us)
                    : null;
                break;
            case "speed":
                _speed = double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
                    ? speed
                    : null;
                break;
            case "progress":
                progress.Report(new FfmpegProgress(_frame, _outTime, _speed, IsEnd: value == "end"));
                break;
        }
    }
}
