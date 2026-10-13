using System.Text.Json.Serialization;
using StickerSnap.Application.App;
using StickerSnap.Application.Validation;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Validation;

namespace StickerSnap.Api.Contracts;

// The results of an export (contract, ExportResult), as Project.exports and an export's job carry them.

/// <summary>The last export of a segment (schema ExportResult).</summary>
/// <param name="SceneRevision">The revision of the scene the export was made from</param>
/// <param name="Stale">The segment was edited after the export</param>
/// <param name="Path">Desktop only: «Показать в папке»</param>
/// <param name="FileUrl">GET /api/v1/files/{fileId}</param>
public sealed record ExportResult(
    string SegmentId,
    int SceneRevision,
    bool Stale,
    string? Path,
    string FileUrl,
    long Bytes,
    OutputSpec Output,
    int Crf,
    IReadOnlyList<Concession> Concessions,
    IReadOnlyList<LadderStep> Ladder,
    ValidationReport Validation)
{
    /// <param name="stale">Whether the segment was edited since: only the project knows</param>
    /// <param name="mode">The path is the desktop's: the browser downloads the file instead (R9)</param>
    public static ExportResult From(SegmentExport export, bool stale, AppMode mode) => new(
        export.SegmentId,
        export.SceneRevision,
        stale,
        mode == AppMode.Desktop ? export.Path : null,
        FileUrls.Of(export.FileId),
        export.Bytes,
        new OutputSpec(export.Output.Width, export.Output.Height, export.Output.Fps, export.Output.DurationSec),
        export.Crf,
        [.. export.Concessions.Select(Concession.Of)],
        [.. export.Ladder.Select(step => new LadderStep([.. step.Rung.Concessions.Select(Concession.Of)], step.Bytes))],
        ValidationReport.From(new FileValidation(export.FileName, export.Bytes, export.Validation)));
}

/// <summary>The address of a rendered file: GET /api/v1/files/{fileId}.</summary>
public static class FileUrls
{
    public static string Of(string fileId) => $"/api/v1/files/{fileId}";
}

/// <summary>What the sticker came out as (schema OutputSpec).</summary>
public sealed record OutputSpec(int Width, int Height, double Fps, double DurationSec);

/// <summary>«Чем пожертвовали» (schema Concession): a step of the fit ladder, told apart by `kind`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FpsConcession), "fps")]
[JsonDerivedType(typeof(CrfConcession), "crf")]
[JsonDerivedType(typeof(ScaleConcession), "scale")]
public abstract record Concession(double From, double To)
{
    public static Concession Of(Core.Render.Concession concession) => concession switch
    {
        Core.Render.Concession.Fps fps => new FpsConcession(fps.From, fps.To),
        Core.Render.Concession.Crf crf => new CrfConcession(crf.From, crf.To),
        Core.Render.Concession.Scale scale => new ScaleConcession(scale.From, scale.To),
        _ => throw new ArgumentOutOfRangeException(nameof(concession), concession, "A concession the contract does not know"),
    };
}

public sealed record FpsConcession(double From, double To) : Concession(From, To);

public sealed record CrfConcession(double From, double To) : Concession(From, To);

public sealed record ScaleConcession(double From, double To) : Concession(From, To);

/// <summary>«Как подбирали» (schema LadderStep): a rung tried and the size it gave.</summary>
public sealed record LadderStep(IReadOnlyList<Concession> Label, long Bytes);

/// <summary>R8, the checklist of artboard 3c (schema ValidationReport): POST /api/v1/validate and an export's check.</summary>
public sealed record ValidationReport(string FileName, long Bytes, ValidationVerdict Verdict, IReadOnlyList<ValidationCheck> Checks)
{
    public static ValidationReport From(FileValidation file) => new(
        file.FileName,
        file.Bytes,
        file.Validation.Verdict,
        file.Validation.Checks.Select(c => new ValidationCheck(c.Rule, c.Status, c.Expected, c.Actual)).ToList());
}

/// <param name="Expected">`≤ 262144`</param>
/// <param name="Actual">`277504`</param>
public sealed record ValidationCheck(ValidationRule Rule, CheckStatus Status, string Expected, string Actual);
