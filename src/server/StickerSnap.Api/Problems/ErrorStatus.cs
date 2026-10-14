using StickerSnap.Core.Errors;

namespace StickerSnap.Api.Problems;

/// <summary>The HTTP status of each code, from the table in the contract's «Ошибки».</summary>
public static class ErrorStatus
{
    public static int Of(ErrorCode code) => code switch
    {
        ErrorCode.SourceUnreadable => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.FormatUnsupported => StatusCodes.Status415UnsupportedMediaType,
        ErrorCode.SourceNotFound => StatusCodes.Status404NotFound,
        ErrorCode.ProjectNotFound => StatusCodes.Status404NotFound,
        ErrorCode.ProjectInvalid => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.ProjectTooNew => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.SaveFailed => StatusCodes.Status507InsufficientStorage,
        ErrorCode.RevisionConflict => StatusCodes.Status409Conflict,
        ErrorCode.DiskFull => StatusCodes.Status507InsufficientStorage,
        ErrorCode.RenderFailed => StatusCodes.Status500InternalServerError,
        ErrorCode.CannotFit => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.FfmpegMissing => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.JobNotFound => StatusCodes.Status404NotFound,
        ErrorCode.FileNotFound => StatusCodes.Status404NotFound,
        ErrorCode.ValidationFailed => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.AccountNotLinked => StatusCodes.Status409Conflict,
        ErrorCode.BotBlocked => StatusCodes.Status403Forbidden,
        ErrorCode.NetworkUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.BotUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorCode.PackNameInvalid => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.PackNameTaken => StatusCodes.Status409Conflict,
        ErrorCode.PackFull => StatusCodes.Status409Conflict,
        ErrorCode.PackNotFound => StatusCodes.Status404NotFound,
        ErrorCode.NotOurPack => StatusCodes.Status422UnprocessableEntity,
        ErrorCode.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCode.TelegramError => StatusCodes.Status502BadGateway,
        ErrorCode.BadRequest => StatusCodes.Status400BadRequest,
        ErrorCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A code without a status in the contract"),
    };
}
