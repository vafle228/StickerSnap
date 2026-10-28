using System.Text.Json.Serialization;
using StickerSnap.Core.Links;
using Domain = StickerSnap.Core.Links;

namespace StickerSnap.Api.Contracts;

/// <summary>POST /api/v1/links/probe and POST /api/v1/downloads (schema LinkRequest).</summary>
/// <param name="Url">The link as it was pasted</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LinkRequest(string Url);

/// <summary>A video by link before it is downloaded (schema LinkVideo, artboard 20·3·4).</summary>
/// <param name="Url">The link to the one video, without `?t=` and the playlist</param>
/// <param name="ThumbnailUrl">The site's preview, https</param>
/// <param name="Bytes">How big the downloaded file is expected to be</param>
/// <param name="Quality">«720p»; for Giphy «MP4» or «WEBP»</param>
public sealed record LinkVideo(
    LinkSite Site,
    string Url,
    string Title,
    double DurationSec,
    string? ThumbnailUrl,
    long Bytes,
    string Quality)
{
    public static LinkVideo From(Domain.LinkVideo video) =>
        new(video.Site, video.Url, video.Title, video.DurationSec, video.ThumbnailUrl, video.Bytes, video.Quality);
}

/// <summary>
/// A download by link (schema Download): the answers of /api/v1/downloads and the event
/// downloadUpdated. Its failure carries the download's id as its trace id.
/// </summary>
/// <param name="EtaSec">How long is left, while downloading</param>
/// <param name="ProjectId">succeeded: the project is in «Проекты» already</param>
public sealed record Download(
    string Id,
    LinkVideo Video,
    DownloadState State,
    long ReceivedBytes,
    double? EtaSec,
    Guid? ProjectId,
    ProblemBody? Error,
    DateTimeOffset CreatedAt)
{
    public static Download From(Domain.Download download) => new(
        download.Id,
        LinkVideo.From(download.Video),
        download.State,
        download.ReceivedBytes,
        download.EtaSec,
        download.ProjectId,
        ProblemBody.From(download.Error, download.Id),
        download.CreatedAt);
}
