using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.App;

/// <summary>What «О программе» and the start of the SPA need to know about the server.</summary>
public sealed record AppInfo(
    string AppVersion,
    FfmpegLookup Ffmpeg,
    string ApiVersion,
    AppMode Mode,
    string BotUsername,
    TelegramLimits Limits);
