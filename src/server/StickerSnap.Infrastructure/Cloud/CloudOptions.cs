namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// Where the cloud service answers: StickerSnap__ServiceUrl. The build decides it (appsettings of
/// the environment): development talks to the service with the development bot, the installed app
/// to the production one. The desktop holds no bot token, only the key of this computer, which
/// the service gives when the computer is linked (<see cref="DeviceCredentials"/>).
/// </summary>
public sealed class CloudOptions
{
    public const string Section = "StickerSnap";

    /// <summary>The service's address without /v1; none means linking, publishing and feedback answer bot_unavailable.</summary>
    public Uri? ServiceUrl { get; set; }
}
