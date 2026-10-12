namespace StickerSnap.Core.Projects;

/// <summary>One sticker cut from the source (contract, Segment).</summary>
/// <param name="Name">«Стикер 1»</param>
/// <param name="InFrame">The first frame of the source it plays, included</param>
/// <param name="OutFrame">The last frame it plays, included</param>
/// <param name="ExcludedFrames">P1: frames between In and Out that are cut out; empty in the MVP</param>
/// <param name="Frame">R3: the part of the picture kept, or how it is squeezed</param>
/// <param name="Speed">From <see cref="MinSpeed"/> to <see cref="MaxSpeed"/>, 1 as recorded</param>
/// <param name="LongSticker">R19: may play longer than Telegram's 3 seconds</param>
/// <param name="AutoFit">R7: the fit ladder brings the file under the limit; false exports it as it comes out</param>
public sealed record Segment(
    string Id,
    string Name,
    int InFrame,
    int OutFrame,
    IReadOnlyList<int> ExcludedFrames,
    SegmentFrame Frame,
    double Speed,
    LoopMode Loop,
    SegmentPreset Preset,
    bool LongSticker,
    bool AutoFit)
{
    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 4;
}

/// <summary>R5: how the sticker loops.</summary>
public enum LoopMode
{
    Normal,

    /// <summary>Forwards, then backwards.</summary>
    Pingpong,
}

/// <summary>R17: a 512 px sticker or a 100×100 custom emoji.</summary>
public enum SegmentPreset
{
    Sticker,
    Emoji,
}
