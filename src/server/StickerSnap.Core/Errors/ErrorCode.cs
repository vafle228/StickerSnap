namespace StickerSnap.Core.Errors;

/// <summary>
/// The closed set of error codes (contract, «Ошибки»). The SPA picks the words and the form of a
/// failure by its code; a new code is a change of the contract first. Written to JSON in
/// snake_case: <see cref="SourceUnreadable"/> is `source_unreadable`.
/// </summary>
public enum ErrorCode
{
    SourceUnreadable,
    FormatUnsupported,
    SourceNotFound,
    ProjectNotFound,
    ProjectInvalid,
    ProjectTooNew,
    SaveFailed,
    RevisionConflict,
    DiskFull,
    RenderFailed,
    CannotFit,
    FfmpegMissing,
    JobNotFound,
    FileNotFound,
    ValidationFailed,
    AccountNotLinked,
    BotBlocked,
    NetworkUnavailable,
    BotUnavailable,
    PackNameInvalid,
    PackNameTaken,
    PackFull,
    PackNotFound,
    NotOurPack,
    RateLimited,
    TelegramError,
    BadRequest,
    Internal,
}
