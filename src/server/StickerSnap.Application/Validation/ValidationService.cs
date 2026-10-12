using StickerSnap.Application.Media;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;

namespace StickerSnap.Application.Validation;

/// <summary>Checks any .webm against Telegram's rules (R8): a file dropped on the window, made anywhere.</summary>
public sealed class ValidationService(SourceFiles files, IMediaProbe probe)
{
    /// <exception cref="AppException">
    /// `format_unsupported` with `params.ext` for a file that is not .webm; `source_not_found`,
    /// `source_unreadable` and `ffmpeg_missing` from the probe; `bad_request` for a path from a browser
    /// </exception>
    public async Task<FileValidation> ValidateAsync(SourceRef source, CancellationToken cancellationToken)
    {
        var path = files.PathOf(source);
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extension != "webm")
            throw new AppException(ErrorCode.FormatUnsupported, $"{Path.GetFileName(path)} is not a .webm")
            {
                Params = new Dictionary<string, object> { ["ext"] = extension },
            };

        var media = await probe.ProbeAsync(path, cancellationToken);
        return new FileValidation(Path.GetFileName(path), media.SizeBytes, StickerValidator.Validate(media, TelegramLimits.Current));
    }
}

/// <summary>The checklist of one file, under its name and size.</summary>
public sealed record FileValidation(string FileName, long Bytes, StickerValidation Validation);
