using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace StickerSnap.Infrastructure.Webm;

/// <summary>
/// Rewrites the length a WEBM declares, in place, as the long sticker of R19 needs (spike B6,
/// 12 October; prototype spoof_duration.py): Telegram takes a video sticker's length from
/// Segment › Info › Duration, players play every frame whatever it says. The DURATION tags, which
/// FFmpeg writes and ffprobe shows, get the same value. Nothing changes length: the float keeps
/// its 4 or 8 bytes and a tag string its room, so the offsets in SeekHead and Cues stay right.
/// A CRC-32 over a changed element would go stale and becomes a Void of the same size. The
/// track's DefaultDuration (its frame rate) is never touched: iOS plays at that rate.
/// </summary>
internal static class WebmDuration
{
    private const uint Segment = 0x18538067;
    private const uint Info = 0x1549A966;
    private const uint TimestampScale = 0x2AD7B1;
    private const uint Duration = 0x4489;
    private const uint Tags = 0x1254C367;
    private const uint SimpleTag = 0x67C8;
    private const uint TagName = 0x45A3;
    private const uint TagString = 0x4487;
    private const uint Crc32 = 0xBF;
    private const byte Void = 0xEC;

    /// <summary>Matroska's default: a timestamp counts milliseconds.</summary>
    private const ulong DefaultScale = 1_000_000;

    /// <summary>
    /// Writes <paramref name="seconds"/> into the Info › Duration of <paramref name="file"/> and
    /// into its DURATION tags.
    /// </summary>
    /// <returns>The values rewritten: the duration and each tag</returns>
    /// <exception cref="FormatException">The file is no Matroska, or declares no Info › Duration</exception>
    public static int Declare(Span<byte> file, double seconds)
    {
        if (!(seconds > 0) || !double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "must be positive");

        var durations = 0;
        var tags = 0;
        foreach (var element in Children(file, SegmentOf(file)))
        {
            if (element.Id == Info)
            {
                var scale = ScaleOf(file, element);
                foreach (var child in Children(file, element))
                {
                    if (child.Id != Duration) continue;
                    WriteFloat(file[child.Data..child.End], seconds * 1e9 / scale);
                    durations++;
                }

                VoidCrc(file, element);
            }
            else if (element.Id == Tags)
            {
                foreach (var tag in Children(file, element))
                {
                    foreach (var child in Children(file, tag))
                        if (child.Id == SimpleTag && WriteDurationTag(file, child, seconds)) tags++;
                    VoidCrc(file, tag);
                }

                VoidCrc(file, element);
            }
        }

        if (durations == 0) throw new FormatException("The WEBM declares no Info › Duration");
        return durations + tags;
    }

    /// <summary>What the file declares in Info › Duration, in seconds; null when it declares nothing.</summary>
    /// <exception cref="FormatException">The file is no Matroska</exception>
    public static double? Declared(ReadOnlySpan<byte> file)
    {
        foreach (var element in Children(file, SegmentOf(file)))
        {
            if (element.Id != Info) continue;
            foreach (var child in Children(file, element))
                if (child.Id == Duration) return ReadFloat(file[child.Data..child.End]) * ScaleOf(file, element) / 1e9;
        }

        return null;
    }

    /// <summary>The DURATION tags' values, as written: `00:00:04.000000000`, without the NULs after.</summary>
    public static IReadOnlyList<string> DurationTags(ReadOnlySpan<byte> file)
    {
        List<string> values = [];
        foreach (var element in Children(file, SegmentOf(file)))
        {
            if (element.Id != Tags) continue;
            foreach (var tag in Children(file, element))
            foreach (var child in Children(file, tag))
            {
                if (child.Id == SimpleTag && DurationTagValue(file, child) is { } value)
                    values.Add(Encoding.ASCII.GetString(file[value.Data..value.End].TrimEnd((byte)0)));
            }
        }

        return values;
    }

    /// <summary>A DURATION tag gets the time as FFmpeg writes it, `00:00:02.900000000`, padded with NULs.</summary>
    private static bool WriteDurationTag(Span<byte> file, Element simpleTag, double seconds)
    {
        if (DurationTagValue(file, simpleTag) is not { } value) return false;

        var whole = TimeSpan.FromSeconds(Math.Floor(seconds));
        var text = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"{(int)whole.TotalHours:00}:{whole.Minutes:00}:{seconds % 60:00.000000000}"));
        var room = file[value.Data..value.End];
        if (text.Length > room.Length) return false;

        room.Clear();
        text.CopyTo(room);
        return true;
    }

    /// <summary>The TagString of a SimpleTag named DURATION; null for any other tag.</summary>
    private static Element? DurationTagValue(ReadOnlySpan<byte> file, Element simpleTag)
    {
        Element? name = null, value = null;
        foreach (var field in Children(file, simpleTag))
        {
            if (field.Id == TagName) name = field;
            else if (field.Id == TagString) value = field;
        }

        return name is { } n && file[n.Data..n.End].TrimEnd((byte)0).SequenceEqual("DURATION"u8) ? value : null;
    }

    private static ulong ScaleOf(ReadOnlySpan<byte> file, Element info)
    {
        foreach (var child in Children(file, info))
        {
            if (child.Id != TimestampScale) continue;
            ulong scale = 0;
            foreach (var b in file[child.Data..child.End]) scale = (scale << 8) | b;
            return scale;
        }

        return DefaultScale;
    }

    /// <summary>The CRC-32 among the children of <paramref name="parent"/>, if any, made a Void of the same size.</summary>
    private static void VoidCrc(Span<byte> file, Element parent)
    {
        foreach (var child in Children(file, parent))
            if (child.Id == Crc32) file[child.Header] = Void;
    }

    private static Element SegmentOf(ReadOnlySpan<byte> file)
    {
        foreach (var element in Children(file, new Element(0, 0, 0, file.Length)))
            if (element.Id == Segment) return element;
        throw new FormatException("The file has no Matroska Segment");
    }

    /// <summary>The elements inside <paramref name="parent"/>; one of unknown size runs to the parent's end.</summary>
    private static List<Element> Children(ReadOnlySpan<byte> file, Element parent)
    {
        List<Element> children = [];
        var position = parent.Data;
        while (position < parent.End)
        {
            var (id, idLength, _) = ReadVint(file, position, keepMarker: true);
            var (size, sizeLength, unknown) = ReadVint(file, position + idLength, keepMarker: false);
            var data = position + idLength + sizeLength;
            if (data > parent.End) throw new FormatException($"The element at {position} runs past its parent");

            // A file cut short (or a size past its parent) ends the element at what there is
            var end = unknown ? parent.End : (int)Math.Min((ulong)parent.End, (ulong)data + size);
            children.Add(new Element((uint)id, position, data, end));
            position = end;
        }

        return children;
    }

    /// <summary>An EBML variable-length number: an id keeps its length marker, a size drops it.</summary>
    private static (ulong Value, int Length, bool Unknown) ReadVint(ReadOnlySpan<byte> file, int position, bool keepMarker)
    {
        if (position >= file.Length) throw new FormatException($"The file ends inside an element header at {position}");
        var first = file[position];
        if (first == 0) throw new FormatException($"No EBML number at {position}");

        var length = BitOperations.LeadingZeroCount((uint)first) - 24 + 1;
        if (position + length > file.Length) throw new FormatException($"The file ends inside an element header at {position}");

        var value = keepMarker ? first : (ulong)(first & (0xFF >> length));
        for (var i = 1; i < length; i++) value = (value << 8) | file[position + i];
        var unknown = !keepMarker && value == (1UL << (7 * length)) - 1;
        return (value, length, unknown);
    }

    private static double ReadFloat(ReadOnlySpan<byte> data) => data.Length switch
    {
        4 => BinaryPrimitives.ReadSingleBigEndian(data),
        8 => BinaryPrimitives.ReadDoubleBigEndian(data),
        _ => throw new FormatException($"A float of {data.Length} bytes"),
    };

    private static void WriteFloat(Span<byte> data, double value)
    {
        switch (data.Length)
        {
            case 4:
                BinaryPrimitives.WriteSingleBigEndian(data, (float)value);
                break;
            case 8:
                BinaryPrimitives.WriteDoubleBigEndian(data, value);
                break;
            default:
                throw new FormatException($"A float of {data.Length} bytes");
        }
    }

    /// <param name="Header">Where its id starts</param>
    /// <param name="Data">Where its content starts</param>
    /// <param name="End">Where its content ends</param>
    private readonly record struct Element(uint Id, int Header, int Data, int End);
}
