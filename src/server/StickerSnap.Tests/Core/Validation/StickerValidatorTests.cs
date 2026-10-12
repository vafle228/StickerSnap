using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;
using Samples = StickerSnap.Tests.Core.Render.Media;

namespace StickerSnap.Tests.Core.Validation;

/// <summary>
/// The validator on the bad reference files of test-scenarios.md, section 8 (each rule goes red on
/// its own), on the edges of each limit, and on a long sticker of R19.
/// </summary>
public sealed class StickerValidatorTests
{
    private static readonly TelegramLimits Limits = TelegramLimits.Current;

    private static readonly MediaInfo Emoji = Samples.Sticker with { Width = 100, Height = 100, SizeBytes = 40_000 };

    private static StickerValidation Validate(MediaInfo media) => StickerValidator.Validate(media, Limits);

    private static RuleCheck Rule(StickerValidation validation, ValidationRule rule) => validation.Checks.Single(c => c.Rule == rule);

    [Fact]
    public void Passes_a_3_second_vp9_square_of_512_px_under_256_kb_on_every_rule_in_the_contract_s_order()
    {
        var validation = Validate(Samples.Sticker);

        validation.Verdict.Should().Be(ValidationVerdict.Pass);
        validation.Preset.Should().Be(SegmentPreset.Sticker);
        validation.Checks.Should().Equal(
            new RuleCheck(ValidationRule.Container, CheckStatus.Pass, "webm", "webm"),
            new RuleCheck(ValidationRule.Codec, CheckStatus.Pass, "vp9", "vp9"),
            new RuleCheck(ValidationRule.Size, CheckStatus.Pass, "≤ 262144", "250000"),
            new RuleCheck(ValidationRule.Dimensions, CheckStatus.Pass, "512", "512×512"),
            new RuleCheck(ValidationRule.Duration, CheckStatus.Pass, "≤ 3", "3"),
            new RuleCheck(ValidationRule.Fps, CheckStatus.Pass, "≤ 30", "30"),
            new RuleCheck(ValidationRule.Audio, CheckStatus.Pass, "none", "none"),
            new RuleCheck(ValidationRule.Alpha, CheckStatus.Pass, "any", "none"));
        validation.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Passes_a_transparent_sticker()
    {
        var validation = Validate(Samples.Sticker with { HasAlpha = true });

        validation.Verdict.Should().Be(ValidationVerdict.Pass);
        Rule(validation, ValidationRule.Alpha).Actual.Should().Be("present");
    }

    [Fact]
    public void Passes_a_custom_emoji_of_100_by_100_px()
    {
        var validation = Validate(Emoji);

        validation.Verdict.Should().Be(ValidationVerdict.Pass);
        validation.Preset.Should().Be(SegmentPreset.Emoji);
        Rule(validation, ValidationRule.Dimensions).Should().Be(new RuleCheck(ValidationRule.Dimensions, CheckStatus.Pass, "100×100", "100×100"));
    }

    [Theory]
    [InlineData(100, 80)]
    [InlineData(64, 64)]
    public void Takes_a_picture_no_larger_than_100_px_for_an_emoji_that_must_be_square(int width, int height)
    {
        var validation = Validate(Emoji with { Width = width, Height = height });

        validation.Preset.Should().Be(SegmentPreset.Emoji);
        Rule(validation, ValidationRule.Dimensions).Should().Be(new RuleCheck(ValidationRule.Dimensions, CheckStatus.Fail, "100×100", $"{width}×{height}"));
    }

    [Theory]
    [InlineData(512, 511)]
    [InlineData(288, 512)]
    [InlineData(512, 101)]
    public void Passes_a_sticker_with_one_side_of_512_px_and_a_shorter_other(int width, int height)
    {
        Rule(Validate(Samples.Sticker with { Width = width, Height = height }), ValidationRule.Dimensions).Status.Should().Be(CheckStatus.Pass);
    }

    [Theory]
    [InlineData(513, 512)]
    [InlineData(511, 511)]
    [InlineData(640, 360)]
    public void Fails_a_sticker_with_no_side_of_exactly_512_px(int width, int height)
    {
        var validation = Validate(Samples.Sticker with { Width = width, Height = height });

        validation.Verdict.Should().Be(ValidationVerdict.Fail);
        Rule(validation, ValidationRule.Dimensions).Should().Be(new RuleCheck(ValidationRule.Dimensions, CheckStatus.Fail, "512", $"{width}×{height}"));
    }

    [Theory]
    [InlineData(262_144, CheckStatus.Pass)]
    [InlineData(262_145, CheckStatus.Fail)]
    [InlineData(300_000, CheckStatus.Fail)]
    public void Holds_the_size_to_256_kb_to_the_byte(long bytes, CheckStatus status)
    {
        Rule(Validate(Samples.Sticker with { SizeBytes = bytes }), ValidationRule.Size)
            .Should().Be(new RuleCheck(ValidationRule.Size, status, "≤ 262144", bytes.ToString()));
    }

    [Theory]
    [InlineData(3.00, CheckStatus.Pass, "3")]
    [InlineData(3.03, CheckStatus.Pass, "3.03")]
    [InlineData(3.04, CheckStatus.Fail, "3.04")]
    [InlineData(4.00, CheckStatus.Fail, "4")]
    public void Allows_a_frame_over_3_seconds_as_a_container_rounds_it_and_no_more(double seconds, CheckStatus status, string actual)
    {
        Rule(Validate(Samples.Sticker with { DurationSec = seconds, DeclaredDurationSec = seconds }), ValidationRule.Duration)
            .Should().Be(new RuleCheck(ValidationRule.Duration, status, "≤ 3", actual));
    }

    [Fact]
    public void Calls_a_sticker_that_declares_3_seconds_over_longer_frames_experimental()
    {
        // @fStikBot's sticker (spike B6): 2.9 s in Info › Duration over 20.7 s of frames at 15 fps
        var validation = Validate(Samples.Sticker with
        {
            Fps = new Rational(15, 1), FrameCount = 311, DurationSec = 20.734, DeclaredDurationSec = 2.9,
        });

        validation.Verdict.Should().Be(ValidationVerdict.Experimental);
        Rule(validation, ValidationRule.Duration).Should().Be(new RuleCheck(ValidationRule.Duration, CheckStatus.Warn, "≤ 3", "20.73"));
    }

    [Fact]
    public void Fails_an_honest_4_second_file_rather_than_calling_it_experimental()
    {
        var validation = Validate(Samples.Sticker with { FrameCount = 120, DurationSec = 4, DeclaredDurationSec = 4 });

        validation.Verdict.Should().Be(ValidationVerdict.Fail);
        Rule(validation, ValidationRule.Duration).Status.Should().Be(CheckStatus.Fail);
    }

    [Fact]
    public void Fails_a_long_sticker_that_breaks_another_rule_too()
    {
        var validation = Validate(Samples.Sticker with { DurationSec = 6, DeclaredDurationSec = 2.9, HasAudio = true });

        validation.Verdict.Should().Be(ValidationVerdict.Fail);
        Rule(validation, ValidationRule.Duration).Status.Should().Be(CheckStatus.Warn);
    }

    [Theory]
    [InlineData(30, 1, CheckStatus.Pass, "30")]
    [InlineData(30000, 1001, CheckStatus.Pass, "29.97")]
    [InlineData(60, 1, CheckStatus.Fail, "60")]
    public void Holds_the_frame_rate_to_30(int num, int den, CheckStatus status, string actual)
    {
        Rule(Validate(Samples.Sticker with { Fps = new Rational(num, den) }), ValidationRule.Fps)
            .Should().Be(new RuleCheck(ValidationRule.Fps, status, "≤ 30", actual));
    }

    [Fact]
    public void Fails_a_sticker_with_a_sound_track()
    {
        var validation = Validate(Samples.Sticker with { HasAudio = true });

        validation.Verdict.Should().Be(ValidationVerdict.Fail);
        Rule(validation, ValidationRule.Audio).Should().Be(new RuleCheck(ValidationRule.Audio, CheckStatus.Fail, "none", "present"));
    }

    [Fact]
    public void Fails_h264_in_mp4_on_both_the_container_and_the_codec()
    {
        var validation = Validate(Samples.Sticker with { Format = MediaFormat.Mp4, Codec = "h264" });

        validation.Checks.Where(c => c.Status == CheckStatus.Fail).Select(c => (c.Rule, c.Expected, c.Actual)).Should().Equal(
            (ValidationRule.Container, "webm", "mp4"),
            (ValidationRule.Codec, "vp9", "h264"));
    }

    [Fact]
    public void Names_every_rule_a_file_breaks_in_english()
    {
        var media = new MediaInfo(
            MediaFormat.Mp4, "h264", 640, 480, new Rational(60, 1), IsVariableFrameRate: false,
            FrameCount: 240, DurationSec: 4, HasAlpha: false, HasAudio: true, SizeBytes: 300_000);

        StickerValidator.Validate(media, SegmentPreset.Sticker, Limits.Sticker).Problems.Should().Equal(
            "container mp4, expected webm",
            "codec h264, expected vp9",
            "size 300000, expected ≤ 262144",
            "dimensions 640×480, expected 512",
            "duration 4, expected ≤ 3",
            "fps 60, expected ≤ 30",
            "audio present, expected none");
    }
}
