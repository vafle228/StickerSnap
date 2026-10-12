using StickerSnap.Core.Render;

namespace StickerSnap.Application.Render;

/// <summary>
/// How far rendering one sticker has got (contract, Job.progress): the share done, what is being
/// done, and which step of the fit ladder is being tried. Reported shares never go back.
/// </summary>
/// <param name="Fraction">0..1 of the sticker, over every pass and every attempt</param>
/// <param name="Attempt">Null before the first step is tried and while the file is checked</param>
public sealed record RenderProgress(double Fraction, RenderStage Stage, RenderAttempt? Attempt);

/// <summary>The stages of a render the contract names.</summary>
public enum RenderStage
{
    /// <summary>Trying the steps of the ladder at their quality to find the first that fits</summary>
    Probe,

    /// <summary>The first of two passes: the encoder reads the clip through</summary>
    Pass1,

    /// <summary>The pass that writes the file: the second of two, or the only one</summary>
    Pass2,

    /// <summary>Reading the file back and checking it against Telegram's rules</summary>
    Validate,
}

/// <summary>«попытка 2, 24 fps»: a step of the fit ladder.</summary>
/// <param name="N">The step's place in the ladder, from 1</param>
/// <param name="Concessions">What the step gives up; none for the first</param>
public sealed record RenderAttempt(int N, IReadOnlyList<Concession> Concessions)
{
    /// <summary>The step at <paramref name="index"/> (from 0) of a ladder.</summary>
    public static RenderAttempt Of(int index, FitRung rung) => new(index + 1, rung.Concessions);
}

/// <summary>
/// A part of a render's progress, from <paramref name="From"/> to <paramref name="To"/> of the
/// whole: a share reported here lands in that part.
/// </summary>
internal sealed record ProgressPart(IProgress<RenderProgress>? Target, double From, double To)
{
    /// <summary>What a part takes of what is left when it is not known how many parts follow.</summary>
    private const double PieceShare = 0.8;

    public static ProgressPart Whole(IProgress<RenderProgress>? target) => new(target, 0, 1);

    public void Report(double fraction, RenderStage stage, RenderAttempt? attempt) =>
        Target?.Report(new RenderProgress(At(fraction), stage, attempt));

    /// <summary>A pass of the media engine, as the stage the contract has for it.</summary>
    public void Report(Media.PassProgress pass, RenderAttempt attempt) => Report(
        (pass.Pass - 1 + pass.Fraction) / pass.Passes,
        pass.Pass < pass.Passes ? RenderStage.Pass1 : RenderStage.Pass2,
        attempt);

    /// <summary>The share <paramref name="from"/>..<paramref name="to"/> of this part.</summary>
    public ProgressPart Part(double from, double to) => this with { From = At(from), To = At(to) };

    /// <summary>
    /// The part number <paramref name="index"/> (from 0) of a run of parts whose number is not
    /// known, such as the attempts of a retarget: the first takes 80%, each next 80% of what is left.
    /// </summary>
    public ProgressPart Piece(int index) =>
        Part(1 - Math.Pow(1 - PieceShare, index), 1 - Math.Pow(1 - PieceShare, index + 1));

    /// <summary>Passes the engine's progress on, mapped into this part, under <paramref name="attempt"/>.</summary>
    public IProgress<Media.PassProgress>? Passes(RenderAttempt attempt) =>
        Target is null ? null : new PassReporter(this, attempt);

    private double At(double fraction) => From + (To - From) * Math.Clamp(fraction, 0, 1);

    private sealed class PassReporter(ProgressPart part, RenderAttempt attempt) : IProgress<Media.PassProgress>
    {
        public void Report(Media.PassProgress value) => part.Report(value, attempt);
    }
}
