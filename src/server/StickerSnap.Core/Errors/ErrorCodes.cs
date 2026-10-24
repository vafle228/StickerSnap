namespace StickerSnap.Core.Errors;

/// <summary>What the contract's table of codes («Ошибки», column reportable) says of each code.</summary>
public static class ErrorCodes
{
    /// <summary>
    /// The error offers «Сообщить об ошибке» (artboard 19·1): the user cannot mend it alone.
    /// An expected error, which the user mends or which leads to its own action, does not.
    /// </summary>
    public static bool IsReportable(this ErrorCode code) => code switch
    {
        ErrorCode.SourceUnreadable => true,
        ErrorCode.FormatUnsupported => false,
        ErrorCode.SourceNotFound => false,
        ErrorCode.ProjectNotFound => false,
        ErrorCode.ProjectInvalid => true,
        ErrorCode.ProjectTooNew => false,
        ErrorCode.SaveFailed => false,
        ErrorCode.RevisionConflict => false,
        ErrorCode.DiskFull => false,
        ErrorCode.RenderFailed => true,
        ErrorCode.CannotFit => false,
        ErrorCode.FfmpegMissing => false,
        ErrorCode.JobNotFound => true,
        ErrorCode.FileNotFound => true,
        ErrorCode.ValidationFailed => false,
        ErrorCode.AccountNotLinked => false,
        ErrorCode.BotBlocked => false,
        ErrorCode.NetworkUnavailable => false,
        ErrorCode.BotUnavailable => false,
        ErrorCode.PackNameInvalid => false,
        ErrorCode.PackNameTaken => false,
        ErrorCode.PackFull => false,
        ErrorCode.PackNotFound => false,
        ErrorCode.NotOurPack => false,
        ErrorCode.RateLimited => false,
        ErrorCode.TelegramError => true,
        ErrorCode.BadRequest => true,
        ErrorCode.Internal => true,
        ErrorCode.LinkUnsupported => false,
        ErrorCode.VideoUnavailable => false,
        ErrorCode.SiteUnreachable => false,
        ErrorCode.SiteChanged => true,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A code without reportable in the contract"),
    };
}
