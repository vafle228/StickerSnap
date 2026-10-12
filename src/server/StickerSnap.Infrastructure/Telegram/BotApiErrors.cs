using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Telegram;

/// <summary>
/// What a refusal of the Bot API means to the SPA. Telegram tells its errors apart only by the
/// description, so the table matches its words; anything it does not list is telegram_error with
/// the description in the log, shown under «Подробности».
/// </summary>
internal static class BotApiErrors
{
    private static readonly (string Words, ErrorCode Code)[] Descriptions =
    [
        ("bot was blocked by the user", ErrorCode.BotBlocked),
        ("sticker set name is already occupied", ErrorCode.PackNameTaken),
        ("STICKERS_TOO_MUCH", ErrorCode.PackFull),
        ("STICKERSET_INVALID", ErrorCode.PackNotFound),
        // The user has never pressed Start, so the bot cannot act for them
        ("PEER_ID_INVALID", ErrorCode.AccountNotLinked),
        ("user not found", ErrorCode.AccountNotLinked),
        ("bot can't initiate conversation with a user", ErrorCode.AccountNotLinked),
    ];

    /// <param name="status">The error_code of the answer, or the HTTP status when there is none</param>
    public static AppException Refused(string method, int status, string? description, int? retryAfter)
    {
        var message = $"Bot API {method} refused: {status} {description}";
        if (status == 429)
        {
            return new AppException(ErrorCode.RateLimited, message)
            {
                Params = new Dictionary<string, object> { ["retryAfterSec"] = Math.Max(retryAfter ?? 1, 1) },
                Log = description,
            };
        }

        var code = Descriptions
            .Where(entry => description?.Contains(entry.Words, StringComparison.OrdinalIgnoreCase) == true)
            .Select(entry => (ErrorCode?)entry.Code)
            .FirstOrDefault() ?? ErrorCode.TelegramError;
        return new AppException(code, message) { Log = description ?? $"HTTP {status}" };
    }

    /// <param name="why">Already free of the token</param>
    public static AppException Unreachable(string method, string why) =>
        new(ErrorCode.NetworkUnavailable, $"Bot API {method} is out of reach: {why}") { Log = why };

    /// <summary>An answer that is not the Bot API's JSON, such as a proxy's error page.</summary>
    public static AppException Garbled(string method, int status) =>
        new(ErrorCode.TelegramError, $"Bot API {method} answered HTTP {status} without its JSON")
        {
            Log = $"HTTP {status} without the Bot API's JSON",
        };
}
