namespace StickerSnap.Application.Media;

/// <summary>
/// The engine that reads and renders video. The use cases know only this and the other ports of
/// Media/; which engine it is (FFmpeg today) is decided by the registration in StickerSnap.Infrastructure.
/// </summary>
public interface IMediaEngine
{
    /// <summary>Whether the engine is there and new enough; asking again after an install sees it.</summary>
    Task<MediaEngineInfo> GetInfoAsync(CancellationToken cancellationToken);
}

public enum MediaEngineStatus
{
    Ok,
    Missing,
    /// <summary>Found, but older than the version the editor needs</summary>
    TooOld,
}

/// <param name="Name">For the log and «О программе»: `FFmpeg`</param>
/// <param name="Version">Null when the engine was not found</param>
public sealed record MediaEngineInfo(string Name, MediaEngineStatus Status, string? Version);
