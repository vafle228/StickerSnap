using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Links;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Links;

/// <summary>
/// Giphy by its own addresses, no key needed: the title from its oEmbed, the file from
/// i.giphy.com — a gif's MP4, a sticker's WEBP, which keeps its transparency. Giphy does not
/// tell a file's length, so the probe downloads the file (a few hundred KB) into a cache and the
/// media engine reads it; the download then takes it from there.
/// </summary>
internal sealed partial class GiphySite(
    IHttpClientFactory clients,
    IMediaProbe probe,
    IOptions<DataOptions> data,
    TimeProvider clock,
    ILogger<GiphySite> logger) : IVideoSite
{
    public const string ClientName = "giphy";

    /// <summary>Giphy's files are small, so a minute is long for any of them.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a probed file waits in the cache for its download.</summary>
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);

    private const int BufferBytes = 81920;

    /// <summary>Where Giphy's files wait between the probe and the download.</summary>
    internal string CacheDir => Path.Combine(data.Value.DataDir, "links", "giphy");

    public LinkSite Site => LinkSite.Giphy;

    public async Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken)
    {
        ClearExpired();
        var variant = link.IsSticker ? "webp" : "mp4";
        var title = await TitleAsync(link, cancellationToken);

        var cached = CachedFile(link.VideoId, variant);
        if (!File.Exists(cached))
        {
            Directory.CreateDirectory(CacheDir);
            var part = $"{cached}.{Guid.NewGuid():N}.part";
            try
            {
                await FetchAsync(link.Url, FileUrl(link.VideoId, variant), part, _ => { }, cancellationToken);
                File.Move(part, cached, overwrite: true);
            }
            finally
            {
                File.Delete(part);
            }
        }

        var media = await probe.ProbeAsync(cached, cancellationToken);
        return new LinkVideo(
            LinkSite.Giphy,
            link.Url,
            title,
            media.DurationSec,
            $"https://i.giphy.com/media/{link.VideoId}/200_s.gif",
            media.SizeBytes,
            variant.ToUpperInvariant(),
            variant,
            variant);
    }

    public async Task<long> DownloadAsync(
        LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var link = VideoLink.Parse(video.Url);
        var cached = CachedFile(link.VideoId, video.Variant);
        try
        {
            if (File.Exists(cached))
            {
                File.Copy(cached, path, overwrite: true);
            }
            else
            {
                await FetchAsync(video.Url, FileUrl(link.VideoId, video.Variant), path, progress, cancellationToken);
            }
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception) && exception is not HttpRequestException)
        {
            throw IoErrors.Of(exception, path);
        }

        var bytes = new FileInfo(path).Length;
        progress(new DownloadProgress(bytes, 0));
        return bytes;
    }

    /// <summary>The cleaned title of oEmbed: «Cat Working GIF - Find &amp; Share on GIPHY» is «Cat Working».</summary>
    internal static string CleanTitle(string? title, string videoId)
    {
        var clean = Kind().Replace(Share().Replace(title ?? "", ""), "").Trim();
        return clean.Length > 0 ? clean : $"Giphy {videoId}";
    }

    private async Task<string> TitleAsync(VideoLink link, CancellationToken cancellationToken)
    {
        var oembed = $"https://giphy.com/services/oembed?url={Uri.EscapeDataString(link.Url)}";
        using var response = await SendAsync(link.Url, oembed, cancellationToken);
        var answer = await response.Content.ReadFromJsonAsync<OEmbed>(cancellationToken);
        return CleanTitle(answer?.Title, link.VideoId);
    }

    /// <summary>Streams <paramref name="url"/> into <paramref name="path"/>, telling the bytes as they come.</summary>
    private async Task FetchAsync(
        string link, string url, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(link, url, cancellationToken);
        var total = response.Content.Headers.ContentLength;
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous);
        var buffer = new byte[BufferBytes];
        long received = 0;
        var started = clock.GetTimestamp();
        try
        {
            while (await body.ReadAsync(buffer, cancellationToken) is var read and > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress(new DownloadProgress(received, Eta(received, total, clock.GetElapsedTime(started))));
            }
        }
        catch (IOException exception) when (!IoErrors.IsDiskFull(exception))
        {
            throw Unreachable(link, exception);
        }
    }

    private static double? Eta(long received, long? total, TimeSpan elapsed) =>
        total is { } all && received > 0 && elapsed > TimeSpan.Zero
            ? Math.Max(0, (all - received) / (received / elapsed.TotalSeconds))
            : null;

    /// <summary>A GET that answered 2xx; Giphy's 404 is a video that is gone.</summary>
    private async Task<HttpResponseMessage> SendAsync(string link, string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // Giphy answers 406 to a request that names no client
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("StickerSnap");
            request.Headers.Accept.ParseAdd("*/*");
            response = await clients.CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(link, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unreachable(link, exception);
        }

        if (response.IsSuccessStatusCode) return response;
        response.Dispose();
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new AppException(ErrorCode.VideoUnavailable, $"{link} is gone from Giphy ({url}: {(int)response.StatusCode})")
            {
                Params = new Dictionary<string, object> { ["reason"] = "gone" },
            };
        logger.LogWarning("Giphy answered {Status} to {Url}", (int)response.StatusCode, url);
        throw new AppException(ErrorCode.SiteChanged, $"Giphy answered {(int)response.StatusCode} to {url}")
        {
            Params = new Dictionary<string, object> { ["site"] = "giphy" },
            Log = $"GET {url}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
        };
    }

    private static AppException Unreachable(string link, Exception exception) =>
        new(ErrorCode.SiteUnreachable, $"Giphy did not answer for {link}: {exception.Message}", exception)
        {
            Params = new Dictionary<string, object> { ["site"] = "giphy" },
        };

    private static string FileUrl(string videoId, string variant) => $"https://i.giphy.com/media/{videoId}/giphy.{variant}";

    private string CachedFile(string videoId, string variant) => Path.Combine(CacheDir, $"{videoId}.{variant}");

    /// <summary>Files probed more than <see cref="CacheLifetime"/> ago; one that cannot go now goes next time.</summary>
    private void ClearExpired()
    {
        if (!Directory.Exists(CacheDir)) return;
        var before = clock.GetUtcNow().UtcDateTime - CacheLifetime;
        foreach (var file in Directory.EnumerateFiles(CacheDir))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < before) File.Delete(file);
            }
            catch (Exception exception) when (IoErrors.IsIoError(exception))
            {
                // In use: the next probe tries again
            }
        }
    }

    private sealed record OEmbed([property: JsonPropertyName("title")] string? Title);

    [GeneratedRegex(@"\s+-\s+Find & Share on GIPHY\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Share();

    /// <summary>«… GIF», «… Sticker», «… GIF by Baywatch Movie».</summary>
    [GeneratedRegex(@"(^|\s+)(GIF|Sticker)(\s+by\s+.+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Kind();
}
