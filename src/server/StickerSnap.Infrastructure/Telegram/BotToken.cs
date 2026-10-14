using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Telegram;

/// <summary>
/// The token of the product bot, @StickerSnapAppBot: the configuration's StickerSnap:BotToken (a
/// user secret in development), otherwise secrets.json in the data directory, which the owner
/// writes by hand: { "botToken": "123456:ABC…" }. The token stays here: it is never logged, never
/// shown and never sent to the SPA, and any text that might hold it goes through <see cref="Redact"/>.
/// </summary>
internal sealed partial class BotToken(IOptions<BotOptions> options, IOptions<DataOptions> data, ILogger<BotToken> logger)
{
    public const string FileName = "secrets.json";

    private const string Hidden = "<bot-token>";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private volatile string? _value;

    /// <summary>
    /// Looked up on first use and again while it is missing, so a token put in place later is
    /// picked up without a restart.
    /// </summary>
    /// <exception cref="AppException">bot_unavailable when no token is configured</exception>
    public string Value => _value ??= Read() ?? throw new AppException(
        ErrorCode.BotUnavailable,
        $"The bot token is not configured: set StickerSnap:BotToken or put {FileName} into the data directory")
    {
        Log = $"The bot token is not configured: no botToken in {FileName} of the data directory",
    };

    /// <summary>The text with the token, once known, cut out: for anything that goes to a log, an exception or a response.</summary>
    public string Redact(string text) =>
        _value is { } token ? text.Replace(token, Hidden, StringComparison.Ordinal) : text;

    private string? Read()
    {
        if (options.Value.BotToken is { Length: > 0 } configured) return Checked(configured, "StickerSnap:BotToken");

        var path = Path.Combine(data.Value.DataDir, FileName);
        if (!File.Exists(path)) return null;

        try
        {
            var secrets = JsonSerializer.Deserialize<Secrets>(File.ReadAllBytes(path), Json);
            return secrets?.BotToken is { Length: > 0 } stored ? Checked(stored, path) : null;
        }
        catch (JsonException)
        {
            // Not the exception itself: its message may quote the file, and so the token
            logger.LogWarning("{Path} is not valid JSON, the bot has no token", path);
            return null;
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "{Path} cannot be read, the bot has no token", path);
            return null;
        }
    }

    /// <summary>The token goes into the Bot API's address, so only the shape @BotFather gives is let in.</summary>
    private string? Checked(string token, string source)
    {
        token = token.Trim();
        if (TokenShape().IsMatch(token)) return token;

        logger.LogWarning("The bot token in {Source} does not look like one from @BotFather and is ignored", source);
        return null;
    }

    [GeneratedRegex(@"^\d+:[A-Za-z0-9_-]+$")]
    private static partial Regex TokenShape();

    private sealed record Secrets(string? BotToken);
}
