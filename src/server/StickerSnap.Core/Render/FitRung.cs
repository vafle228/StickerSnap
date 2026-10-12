namespace StickerSnap.Core.Render;

/// <summary>One step of the fit ladder: the frame rate, the quality and the detail a sticker is rendered at.</summary>
/// <param name="Crf">The quality promised: the final render is at least this good</param>
/// <param name="Detail">The size the picture is shrunk to before it is brought back to the sticker's size</param>
/// <param name="Concessions">What this step gives up against the first one; empty for the first</param>
public sealed record FitRung(int Fps, int Crf, PixelSize Detail, IReadOnlyList<Concession> Concessions)
{
    public override string ToString() =>
        Concessions.Count == 0 ? $"{Fps} fps, crf {Crf}" : string.Join(", ", Concessions);
}
