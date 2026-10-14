using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>
/// The steps a fit ladder may take, as data, so that a kind of sticker with other needs (the long
/// sticker of R19 wants lower frame rates from the start) brings its own.
/// </summary>
/// <param name="FrameRates">
/// From the highest down: the first caps the sticker's rate, the rest are tried below the rate it
/// starts at
/// </param>
/// <param name="Crfs">
/// From the best up: the first is the quality the sticker is promised, each next one is a concession
/// </param>
/// <param name="DetailScales">
/// Shares of the sticker's side the picture is shrunk to before it is brought back, at the last CRF
/// </param>
public sealed record LadderProfile(IReadOnlyList<int> FrameRates, IReadOnlyList<int> Crfs, IReadOnlyList<double> DetailScales)
{
    /// <summary>
    /// A sticker of up to 3 s (fit spike, 12 October): 30 → 24 → 20 fps at CRF 40, then CRF 46 and
    /// 52, then 7/8 and 3/4 of the side. CRF 40 is about the quality of the busiest clip of the test
    /// corpus in two passes, which the owner marked acceptable like all 30, so the ladder gives
    /// nothing up below what was already accepted. A CRF step of 6 takes off about a third of the
    /// size, a frame rate step 5–15%, a detail step only 4%: it is the last resort, before blocks.
    /// </summary>
    public static readonly LadderProfile Sticker = new([30, 24, 20], [40, 46, 52], [0.875, 0.75]);

    /// <summary>
    /// A long sticker (R19) of up to 10 s: the same steps with the frame rate going on down to
    /// 15 and 12 fps before the CRF rises. Its 256 KB are spread over up to three times as many
    /// seconds, and a rate step costs a long clip less than a CRF one; @fStikBot fits 20 s at 15 fps
    /// (spike B6, 12 October).
    /// </summary>
    public static readonly LadderProfile Long = new([30, 24, 20, 15, 12], [40, 46, 52], [0.875, 0.75]);

    /// <summary>
    /// The ladder of a sticker: <see cref="Long"/> when it plays longer than Telegram's limit,
    /// so a long sticker that is short anyway climbs the ladder of any other.
    /// </summary>
    public static LadderProfile For(StickerSpec spec, PresetLimits limits) =>
        LongSticker.DeclaredFor(spec.DurationSec, limits) is null ? Sticker : Long;
}
