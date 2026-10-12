using System.Buffers.Binary;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Real .webm files made by FFmpeg, read by the probe and judged by the validator: the bad reference
/// files of test-scenarios.md, section 8, each red on its own rule, and a long sticker of R19.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealValidationTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<StickerValidation> ValidateAsync(string file) =>
        StickerValidator.Validate(await ffmpeg.Probe.ProbeAsync(file, Cancel), TelegramLimits.Current);

    private Task<string> MakeAsync(string name, string source, params string[] output) =>
        ffmpeg.MakeAsync(name, ["-f", "lavfi", "-i", source, .. output]);

    private static string[] Vp9 => ["-c:v", "libvpx-vp9", "-b:v", "150k"];

    [Fact]
    public async Task Passes_a_3_second_sticker_and_a_custom_emoji()
    {
        var sticker = await MakeAsync("sticker.webm", "testsrc2=s=512x384:r=30:d=3", Vp9);
        var emoji = await MakeAsync("emoji.webm", "testsrc2=s=100x100:r=30:d=2", Vp9);

        (await ValidateAsync(sticker)).Verdict.Should().Be(ValidationVerdict.Pass);
        (await ValidateAsync(emoji)).Should().BeEquivalentTo(new { Verdict = ValidationVerdict.Pass, Preset = SegmentPreset.Emoji });
    }

    [Fact]
    public async Task Fails_each_bad_reference_file_on_its_own_rule()
    {
        var withSound = await ffmpeg.MakeAsync("sound.webm",
            ["-f", "lavfi", "-i", "testsrc2=s=512x512:r=30:d=2", "-f", "lavfi", "-i", "sine=d=2", .. Vp9, "-c:a", "libopus", "-shortest"]);
        var h264 = Rename(await MakeAsync("h264.mp4", "testsrc2=s=512x512:r=30:d=2", "-c:v", "libopenh264", "-b:v", "150k"), "h264.webm");
        var fourSeconds = await MakeAsync("four.webm", "testsrc2=s=512x512:r=30:d=4", Vp9);
        var heavy = await MakeAsync("heavy.webm", "testsrc2=s=512x512:r=30:d=3", "-c:v", "libvpx-vp9", "-b:v", "2M");
        var wide = await MakeAsync("wide.webm", "testsrc2=s=512x288:r=30:d=2", ["-vf", "scale=513:288,setsar=1", .. Vp9]);

        (await FailedAsync(withSound)).Should().Equal(ValidationRule.Audio);
        (await FailedAsync(h264)).Should().Equal(ValidationRule.Container, ValidationRule.Codec);
        (await FailedAsync(fourSeconds)).Should().Equal(ValidationRule.Duration);
        (await FailedAsync(heavy)).Should().Equal(ValidationRule.Size);
        (await FailedAsync(wide)).Should().Equal(ValidationRule.Dimensions);
    }

    /// <summary>The trick of spike B6: 6 s of frames under a header that declares 2.9 s.</summary>
    [Fact]
    public async Task Calls_a_long_sticker_with_a_header_of_2_9_seconds_experimental()
    {
        var file = await MakeAsync("long.webm", "testsrc2=s=512x512:r=15:d=6", Vp9);
        await DeclareDurationAsync(file, 2.9);

        var validation = await ValidateAsync(file);

        validation.Verdict.Should().Be(ValidationVerdict.Experimental);
        validation.Checks.Single(c => c.Rule == ValidationRule.Duration)
            .Should().Be(new RuleCheck(ValidationRule.Duration, CheckStatus.Warn, "≤ 3", "6"));
    }

    private async Task<IEnumerable<ValidationRule>> FailedAsync(string file) =>
        (await ValidateAsync(file)).Checks.Where(c => c.Status == CheckStatus.Fail).Select(c => c.Rule);

    private static string Rename(string path, string name)
    {
        var renamed = Path.Combine(Path.GetDirectoryName(path)!, name);
        File.Move(path, renamed);
        return renamed;
    }

    /// <summary>
    /// Writes <paramref name="seconds"/> into Segment › Info › Duration, the float FFmpeg's muxer
    /// leaves there in milliseconds of the default TimecodeScale; the frames stay as they are.
    /// </summary>
    private static async Task DeclareDurationAsync(string path, double seconds)
    {
        var bytes = await File.ReadAllBytesAsync(path, Cancel);
        var info = bytes.AsSpan().IndexOf((ReadOnlySpan<byte>)[0x15, 0x49, 0xA9, 0x66]);
        var duration = info + bytes.AsSpan(info).IndexOf((ReadOnlySpan<byte>)[0x44, 0x89]);
        var size = bytes[duration + 2] & 0x7F;
        var value = bytes.AsSpan(duration + 3, size);
        if (size == 8) BinaryPrimitives.WriteDoubleBigEndian(value, seconds * 1000);
        else BinaryPrimitives.WriteSingleBigEndian(value, (float)(seconds * 1000));
        await File.WriteAllBytesAsync(path, bytes, Cancel);
    }
}
