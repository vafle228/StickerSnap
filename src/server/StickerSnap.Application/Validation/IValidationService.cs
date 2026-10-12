using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Application.Validation;

/// <summary>Checks any .webm against Telegram's rules (R8).</summary>
public interface IValidationService
{
    /// <exception cref="AppException">
    /// `format_unsupported` with `params.ext` for a file that is not .webm; `source_not_found`,
    /// `source_unreadable` and `ffmpeg_missing` from the probe; `bad_request` for a path from a browser
    /// </exception>
    Task<FileValidation> ValidateAsync(SourceRef source, CancellationToken cancellationToken);
}
