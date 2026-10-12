namespace StickerSnap.Core.Validation;

/// <summary>A rule of Telegram's table for video stickers (contract, ValidationReport.checks.rule).</summary>
public enum ValidationRule
{
    Container,
    Codec,
    Size,
    Dimensions,
    Duration,
    Fps,
    Audio,
    Alpha,
}

/// <summary>`warn`: allowed, but outside the documented rules (contract, ValidationReport.checks.status).</summary>
public enum CheckStatus
{
    Pass,
    Fail,
    Warn,
}

/// <summary>`experimental`: a long sticker of R19 (contract, ValidationVerdict).</summary>
public enum ValidationVerdict
{
    Pass,
    Fail,
    Experimental,
}
