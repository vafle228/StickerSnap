using System.Text;
using StickerSnap.Core.Media;

namespace StickerSnap.Tests.Core.Media;

/// <summary>Telling an upload's format by its first bytes, whatever its name says.</summary>
public sealed class MediaFormatsTests
{
    private static byte[] Bytes(params object[] parts) =>
    [
        .. parts.SelectMany(part => part switch
        {
            string text => Encoding.ASCII.GetBytes(text),
            byte[] raw => raw,
            _ => throw new ArgumentException("text or bytes", nameof(parts)),
        }),
    ];

    private static readonly byte[] Ebml = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0x82, 0x84];
    private static readonly byte[] Size = [0x00, 0x00, 0x00, 0x20];

    public static TheoryData<string, byte[], MediaFormat?> Heads => new()
    {
        { "GIF89a", Bytes("GIF89a", new byte[10]), MediaFormat.Gif },
        { "GIF87a", Bytes("GIF87a", new byte[10]), MediaFormat.Gif },
        { "WEBP", Bytes("RIFF", new byte[] { 1, 2, 3, 4 }, "WEBPVP8X"), MediaFormat.Webp },
        { "WEBM", Bytes(Ebml, "webm", new byte[8]), MediaFormat.Webm },
        { "MKV is not WEBM", Bytes(Ebml, "matroska", new byte[8]), null },
        { "MP4", Bytes(Size, "ftypisom", new byte[8]), MediaFormat.Mp4 },
        { "MP4 of an iPhone", Bytes(Size, "ftypmp42", new byte[8]), MediaFormat.Mp4 },
        { "MOV", Bytes(Size, "ftypqt  ", new byte[8]), MediaFormat.Mov },
        { "AVI", Bytes("RIFF", new byte[] { 1, 2, 3, 4 }, "AVI LIST"), null },
        { "text", Bytes("hello, this is not a video"), null },
        { "nothing", [], null },
        { "a GIF cut short", Bytes("GIF8"), null },
    };

    [Theory]
    [MemberData(nameof(Heads))]
    public void Tells_the_format_by_the_content(string what, byte[] head, MediaFormat? format)
    {
        MediaFormats.Sniff(head).Should().Be(format, what);
    }

    [Theory]
    [InlineData(MediaFormat.Mp4, "mp4")]
    [InlineData(MediaFormat.Mov, "mov")]
    [InlineData(MediaFormat.Webm, "webm")]
    [InlineData(MediaFormat.Gif, "gif")]
    [InlineData(MediaFormat.Webp, "webp")]
    public void Saves_each_format_with_an_extension_the_probe_accepts(MediaFormat format, string extension)
    {
        MediaFormats.ExtensionOf(format).Should().Be(extension);
        MediaFormats.IsSupportedExtension(extension).Should().BeTrue();
    }
}
