namespace StickerSnap.Core.Render;

/// <summary>The size, rate and length of a sticker (contract, OutputSpec).</summary>
public sealed record StickerOutput(int Width, int Height, int Fps, double DurationSec);

/// <summary>A step of the fit ladder tried, and what the sticker weighs there (contract, LadderStep).</summary>
/// <param name="Bytes">Estimated for a step passed by; the file's own size for the step a final render took</param>
public sealed record LadderTry(FitRung Rung, long Bytes);
