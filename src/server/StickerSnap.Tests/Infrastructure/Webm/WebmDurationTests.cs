using System.Buffers.Binary;
using System.Text;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Webm;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Webm;

/// <summary>
/// The patch of a long sticker's header (R19) on a 4 s WEBM the pinned FFmpeg wrote (TestData/Webm,
/// 961 bytes) and on headers built by hand for what FFmpeg does not write: a CRC-32, a 4-byte float.
/// </summary>
public sealed class WebmDurationTests
{
    private const string Sample = "Webm/teal-4s.webm";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void Declares_2_9_seconds_in_the_header_and_the_tag_of_a_file_ffmpeg_wrote()
    {
        var file = TestFiles.ReadBytes(Sample);
        WebmDuration.Declared(file).Should().BeApproximately(4, 1e-9);

        var rewritten = WebmDuration.Declare(file, 2.9);

        rewritten.Should().Be(2);
        WebmDuration.Declared(file).Should().BeApproximately(2.9, 1e-9);
        WebmDuration.DurationTags(file).Should().Equal("00:00:02.900000000");
    }

    [Fact]
    public void Changes_only_the_bytes_of_the_length_so_the_frames_and_every_offset_stay()
    {
        var original = TestFiles.ReadBytes(Sample);
        var file = original.ToArray();

        WebmDuration.Declare(file, 2.9);

        file.Should().HaveCount(original.Length);
        var changed = Enumerable.Range(0, file.Length).Where(i => file[i] != original[i]).ToList();
        // At most the 8 bytes of the float and the 18 characters of the tag
        changed.Should().HaveCountLessThanOrEqualTo(8 + 18);
        (changed[^1] - changed[0]).Should().BeLessThan(original.Length / 2, "both values sit in the header, before the frames");
    }

    [Fact]
    public void Turns_a_crc_over_the_info_into_a_void_of_the_same_size_and_writes_a_4_byte_float()
    {
        var file = Matroska(Info(crc: true, Float4(4000f)), Tags("00:00:04.000000000\0"));
        var length = file.Length;

        WebmDuration.Declare(file, 2.9);

        file.Should().HaveCount(length);
        file.Should().NotContain(0xBF, "the CRC-32 id is gone").And.Contain(0xEC);
        WebmDuration.Declared(file).Should().BeApproximately(2.9, 1e-4);
        WebmDuration.DurationTags(file).Should().Equal("00:00:02.900000000");
    }

    [Fact]
    public void Keeps_a_tag_string_too_short_for_the_new_time_as_it_is()
    {
        var file = Matroska(Info(crc: false, Float4(4000f)), Tags("4s"));

        WebmDuration.Declare(file, 2.9).Should().Be(1);

        WebmDuration.DurationTags(file).Should().Equal("4s");
    }

    [Fact]
    public void Turns_down_a_file_that_declares_no_length()
    {
        var file = Matroska(Element(0x1549A966, Element(0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 })));

        var declare = () => WebmDuration.Declare(file, 2.9);

        declare.Should().Throw<FormatException>();
    }

    [Fact]
    public void Turns_down_a_file_that_is_no_matroska()
    {
        var declare = () => WebmDuration.Declare(Encoding.ASCII.GetBytes("GIF89a, not a sticker"), 2.9);

        declare.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task Rewrites_a_sticker_on_disk_in_place()
    {
        var directory = Directory.CreateTempSubdirectory("stickersnap-webm-");
        try
        {
            var path = await TestFiles.CopyToAsync(Sample, directory.FullName, Cancel);

            await new WebmStickerHeader().DeclareDurationAsync(path, 2.9, Cancel);

            var file = await File.ReadAllBytesAsync(path, Cancel);
            file.Should().HaveCount(TestFiles.ReadBytes(Sample).Length);
            WebmDuration.Declared(file).Should().BeApproximately(2.9, 1e-9);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Fails_a_render_whose_file_cannot_be_rewritten()
    {
        var directory = Directory.CreateTempSubdirectory("stickersnap-webm-");
        try
        {
            var path = Path.Combine(directory.FullName, "broken.webm");
            await File.WriteAllTextAsync(path, "not a sticker", Cancel);

            var declare = () => new WebmStickerHeader().DeclareDurationAsync(path, 2.9, Cancel);

            (await declare.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.RenderFailed);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static byte[] Matroska(params byte[][] children) =>
        [.. Element(0x1A45DFA3, Element(0x4282, "webm"u8.ToArray())), .. Element(0x18538067, children)];

    private static byte[] Info(bool crc, byte[] duration)
    {
        List<byte[]> children = [];
        if (crc) children.Add(Element(0xBF, new byte[] { 0x12, 0x34, 0x56, 0x78 }));
        children.Add(Element(0x2AD7B1, new byte[] { 0x0F, 0x42, 0x40 }));
        children.Add(Element(0x4489, duration));
        return Element(0x1549A966, [.. children]);
    }

    private static byte[] Tags(string duration) =>
        Element(0x1254C367, Element(0x7373, Element(0x67C8,
            Element(0x45A3, "DURATION"u8.ToArray()),
            Element(0x4487, Encoding.ASCII.GetBytes(duration)))));

    private static byte[] Float4(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return bytes;
    }

    /// <summary>An EBML element: its id as written, a one-byte size (all here are under 127 bytes), its content.</summary>
    private static byte[] Element(uint id, params byte[][] content)
    {
        var data = content.SelectMany(part => part).ToArray();
        if (data.Length >= 127) throw new ArgumentException("Too long for a one-byte size", nameof(content));
        var idBytes = BitConverter.GetBytes(id).Reverse().SkipWhile(b => b == 0).ToArray();
        return [.. idBytes, (byte)(0x80 | data.Length), .. data];
    }
}
