using System.Globalization;
using System.Text.Json;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// What a refusal of the cloud service means to the SPA: the service's codes
/// (bot-service.yaml, ErrorCode) turned into the desktop's (openapi.yaml, ErrorCode). The
/// service's words go to the log only.
/// </summary>
internal static class CloudErrors
{
    private static readonly Dictionary<string, ErrorCode> Codes = new(StringComparer.Ordinal)
    {
        // The key was revoked from the phone, or the computer was never linked
        ["unauthorized"] = ErrorCode.AccountNotLinked,
        ["device_not_found"] = ErrorCode.AccountNotLinked,
        ["bot_blocked"] = ErrorCode.BotBlocked,
        ["pack_not_found"] = ErrorCode.PackNotFound,
        ["pack_name_taken"] = ErrorCode.PackNameTaken,
        ["pack_full"] = ErrorCode.PackFull,
        ["pack_name_invalid"] = ErrorCode.PackNameInvalid,
        ["validation_failed"] = ErrorCode.ValidationFailed,
        ["rate_limited"] = ErrorCode.RateLimited,
        // The sticker left the pack in the messenger: what the Bot API's refusal used to be
        ["sticker_not_found"] = ErrorCode.TelegramError,
        ["telegram_error"] = ErrorCode.TelegramError,
        // The service is up but broken
        ["internal"] = ErrorCode.BotUnavailable,
        // A request the service did not take is this app's mistake, not the user's
        ["bad_request"] = ErrorCode.Internal,
        ["link_not_found"] = ErrorCode.Internal,
        ["pack_in_telegram"] = ErrorCode.Internal,
    };

    /// <param name="operation">The operationId of bot-service.yaml, such as createPack</param>
    /// <param name="problem">The answer's Problem Details; null when the answer had none</param>
    /// <param name="retryAfter">The Retry-After header in seconds, if any</param>
    public static AppException Refused(string operation, int status, CloudProblem? problem, int? retryAfter)
    {
        var words = problem is null ? $"HTTP {status}" : $"{status} {problem.Code}: {problem.Detail ?? problem.Title}";
        var message = $"The cloud service refused {operation}: {words}";
        var code = problem?.Code is { } known && Codes.TryGetValue(known, out var mapped) ? mapped : ByStatus(status);

        if (code == ErrorCode.RateLimited)
        {
            var seconds = RetryAfterOf(problem) ?? retryAfter ?? 1;
            return new AppException(code, message)
            {
                Params = new Dictionary<string, object> { ["retryAfterSec"] = Math.Max(seconds, 1) },
                Log = words,
            };
        }
        return new AppException(code, message) { Log = words };
    }

    /// <summary>No connection, or no answer in time: the user's network or the service, the app cannot tell.</summary>
    public static AppException Unreachable(string operation, string why) =>
        new(ErrorCode.NetworkUnavailable, $"The cloud service is out of reach for {operation}: {why}") { Log = why };

    /// <summary>An answer that is not the service's JSON, such as a proxy's error page.</summary>
    public static AppException Garbled(string operation, int status) =>
        new(ErrorCode.BotUnavailable, $"The cloud service answered {operation} with HTTP {status} without its JSON")
        {
            Log = $"HTTP {status} without the service's JSON",
        };

    /// <summary>This build knows no address of the service.</summary>
    public static AppException NotConfigured() =>
        new(ErrorCode.BotUnavailable, $"The cloud service's address is not configured: set {CloudOptions.Section}:{nameof(CloudOptions.ServiceUrl)}")
        {
            Log = "The cloud service's address is not configured",
        };

    /// <summary>A call that acts for the user, made before the computer is linked.</summary>
    public static AppException NotLinked(string operation) =>
        new(ErrorCode.AccountNotLinked, $"{operation} needs this computer to be linked, and it has no key");

    /// <summary>
    /// A code the service may add later, or an answer without one, such as a proxy's 502 while the
    /// service is down: what its status says.
    /// </summary>
    private static ErrorCode ByStatus(int status) => status switch
    {
        401 => ErrorCode.AccountNotLinked,
        429 => ErrorCode.RateLimited,
        >= 500 => ErrorCode.BotUnavailable,
        _ => ErrorCode.Internal,
    };

    private static int? RetryAfterOf(CloudProblem? problem)
    {
        if (problem?.Params is not { } parameters || !parameters.TryGetValue("retryAfterSec", out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => (int)Math.Ceiling(number),
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }
}
