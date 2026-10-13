using System.Text.Json.Serialization;
using StickerSnap.Application.Estimates;
using StickerSnap.Core.Render;

namespace StickerSnap.Api.Contracts;

/// <summary>The body of POST /projects/{id}/estimate (schema EstimateRequest): a segment, saved or not.</summary>
public sealed record EstimateRequest(Segment Segment);

/// <summary>The size forecast of a segment (schema Estimate).</summary>
/// <param name="LimitBytes">The preset's limit, as GET /api/v1/app has it</param>
/// <param name="LikelyConcessions">«Скорее всего, снизим fps до 24»</param>
/// <param name="Suggestions">One-click edits of 3c G</param>
public sealed record Estimate(
    long Bytes,
    long LimitBytes,
    bool Fits,
    OutputSpec Output,
    IReadOnlyList<Concession> LikelyConcessions,
    IReadOnlyList<Suggestion> Suggestions)
{
    public static Estimate From(SegmentEstimate estimate) => new(
        estimate.Bytes,
        estimate.LimitBytes,
        estimate.Fits,
        new OutputSpec(estimate.Output.Width, estimate.Output.Height, estimate.Output.Fps, estimate.Output.DurationSec),
        [.. estimate.LikelyConcessions.Select(Concession.Of)],
        [.. estimate.Suggestions.Select(Suggestion.Of)]);
}

/// <summary>«Укоротить» or «Ускорить», told apart by `kind`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TrimSuggestion), "trim")]
[JsonDerivedType(typeof(SpeedSuggestion), "speed")]
public abstract record Suggestion
{
    public static Suggestion Of(SizeSuggestion suggestion) => suggestion switch
    {
        SizeSuggestion.Trim trim => new TrimSuggestion(trim.OutFrame, trim.DurationSec),
        SizeSuggestion.Speed speed => new SpeedSuggestion(speed.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(suggestion), suggestion, "A suggestion the contract does not know"),
    };
}

/// <param name="DurationSec">How long the sticker then plays</param>
public sealed record TrimSuggestion(int OutFrame, double DurationSec) : Suggestion;

public sealed record SpeedSuggestion(double Speed) : Suggestion;
