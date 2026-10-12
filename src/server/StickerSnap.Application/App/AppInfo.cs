using StickerSnap.Application.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.App;

/// <summary>What «О программе» and the start of the SPA need to know about the server.</summary>
public sealed record AppInfo(
    string AppVersion,
    MediaEngineInfo Engine,
    string ApiVersion,
    AppMode Mode,
    string BotUsername,
    TelegramLimits Limits);
