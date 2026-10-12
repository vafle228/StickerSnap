using System.Text.Json.Serialization;
using StickerSnap.Application.App;
using StickerSnap.Application.Media;

namespace StickerSnap.Api.Contracts;

/// <summary>GET /api/v1/app (schema AppInfo).</summary>
public sealed record AppInfo(
    string AppVersion,
    FfmpegInfo Ffmpeg,
    string ApiVersion,
    AppMode Mode,
    string BotUsername,
    TelegramLimits Limits)
{
    public static AppInfo From(Application.App.AppInfo app) => new(
        app.AppVersion,
        new FfmpegInfo(app.Engine.Status, app.Engine.Version),
        app.ApiVersion,
        app.Mode,
        app.BotUsername,
        new TelegramLimits(
            PresetLimits.From(app.Limits.Sticker),
            PresetLimits.From(app.Limits.Emoji),
            new PackCapacity(app.Limits.RegularPackCapacity, app.Limits.EmojiPackCapacity)));
}

/// <summary>The contract names the media engine `ffmpeg`, after the one the app ships with.</summary>
/// <param name="Version">Absent when the engine was not found</param>
public sealed record FfmpegInfo(MediaEngineStatus Status, string? Version);

public sealed record TelegramLimits(PresetLimits Sticker, PresetLimits Emoji, PackCapacity PackCapacity);

public sealed record PresetLimits(int MaxBytes, int Side, double MaxDurationSec, int MaxFps)
{
    public static PresetLimits From(Core.Telegram.PresetLimits limits) =>
        new(limits.MaxBytes, limits.Side, limits.MaxDurationSec, limits.MaxFps);
}

/// <summary>Named after the sticker types: `regular` and `custom_emoji`.</summary>
public sealed record PackCapacity(int Regular, [property: JsonPropertyName("custom_emoji")] int CustomEmoji);
