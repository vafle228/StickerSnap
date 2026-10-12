using System.Globalization;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Validation;

/// <summary>
/// Telegram's rules for a video sticker (R8), applied to what the media engine read from a file:
/// VP9 in WEBM, the preset's size, side, length and frame rate, no sound; alpha is allowed. The
/// same check passes a dropped .webm, a rendered sticker and a sticker about to be published.
/// </summary>
public static class StickerValidator
{
    /// <summary>A frame at 30 fps: a container's duration rounds up to it, so 3.03 s still passes.</summary>
    private const double DurationSlackSec = 1.0 / 30;

    /// <summary>30000/1001 and the like stay under a rate written as a whole number.</summary>
    private const double FpsSlack = 0.01;

    private const string None = "none";
    private const string Present = "present";

    /// <summary>
    /// The preset a file is meant for, told by its size (contract, G4): a picture no larger than
    /// the emoji's side is a custom emoji, anything larger a sticker.
    /// </summary>
    public static SegmentPreset PresetOf(MediaInfo media, TelegramLimits limits) =>
        Math.Max(media.Width, media.Height) <= limits.Emoji.Side ? SegmentPreset.Emoji : SegmentPreset.Sticker;

    /// <summary>A file of unknown purpose, such as a .webm dropped on the window: its preset by <see cref="PresetOf"/>.</summary>
    public static StickerValidation Validate(MediaInfo media, TelegramLimits limits)
    {
        var preset = PresetOf(media, limits);
        return Validate(media, preset, limits.For(preset));
    }

    /// <summary>Every rule in the contract's order; the verdict fails on any failed rule.</summary>
    public static StickerValidation Validate(MediaInfo media, SegmentPreset preset, PresetLimits limits)
    {
        RuleCheck[] checks =
        [
            Check(ValidationRule.Container, media.Format == MediaFormat.Webm, "webm", media.Format.ToString().ToLowerInvariant()),
            Check(ValidationRule.Codec, media.Codec == "vp9", "vp9", media.Codec),
            Check(ValidationRule.Size, media.SizeBytes <= limits.MaxBytes, $"≤ {limits.MaxBytes}", Number(media.SizeBytes)),
            Dimensions(media, preset, limits),
            Duration(media, limits),
            Check(ValidationRule.Fps, media.Fps.ToDouble() <= limits.MaxFps + FpsSlack, $"≤ {limits.MaxFps}", Number(media.Fps.ToDouble())),
            Check(ValidationRule.Audio, !media.HasAudio, None, media.HasAudio ? Present : None),
            new RuleCheck(ValidationRule.Alpha, CheckStatus.Pass, "any", media.HasAlpha ? Present : None),
        ];

        var verdict = checks.Any(c => c.Status == CheckStatus.Fail) ? ValidationVerdict.Fail
            : checks.Any(c => c.Status == CheckStatus.Warn) ? ValidationVerdict.Experimental
            : ValidationVerdict.Pass;
        return new StickerValidation(preset, verdict, checks);
    }

    /// <summary>A sticker has one side of exactly the preset's and the other no longer; an emoji is a square of it.</summary>
    private static RuleCheck Dimensions(MediaInfo media, SegmentPreset preset, PresetLimits limits)
    {
        var actual = $"{media.Width}×{media.Height}";
        return preset == SegmentPreset.Emoji
            ? Check(ValidationRule.Dimensions, media.Width == limits.Side && media.Height == limits.Side, $"{limits.Side}×{limits.Side}", actual)
            : Check(ValidationRule.Dimensions, Math.Max(media.Width, media.Height) == limits.Side, Number(limits.Side), actual);
    }

    /// <summary>
    /// Telegram reads the length the header declares, a player plays every frame. A header over the
    /// limit fails; a header within it over frames that run longer is the long sticker of R19,
    /// which works but is not in Telegram's rules (spike B6), so it warns.
    /// </summary>
    private static RuleCheck Duration(MediaInfo media, PresetLimits limits)
    {
        var limit = limits.MaxDurationSec + DurationSlackSec;
        var declared = media.DeclaredDurationSec ?? media.DurationSec;
        var status = declared > limit ? CheckStatus.Fail
            : media.DurationSec > limit ? CheckStatus.Warn
            : CheckStatus.Pass;
        return new RuleCheck(ValidationRule.Duration, status, $"≤ {Number(limits.MaxDurationSec)}", Number(media.DurationSec));
    }

    private static RuleCheck Check(ValidationRule rule, bool passes, string expected, string actual) =>
        new(rule, passes ? CheckStatus.Pass : CheckStatus.Fail, expected, actual);

    /// <summary>`2.97`, `29.97`, `262144`: a dot and at most two decimals, whatever the culture.</summary>
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>The checklist of one file and the preset it was checked against.</summary>
public sealed record StickerValidation(SegmentPreset Preset, ValidationVerdict Verdict, IReadOnlyList<RuleCheck> Checks)
{
    /// <summary>The failed rules in English, for a log or a report: `size 300000, expected ≤ 262144`.</summary>
    public IReadOnlyList<string> Problems => Checks
        .Where(c => c.Status == CheckStatus.Fail)
        .Select(c => $"{c.Rule.ToString().ToLowerInvariant()} {c.Actual}, expected {c.Expected}")
        .ToList();
}
