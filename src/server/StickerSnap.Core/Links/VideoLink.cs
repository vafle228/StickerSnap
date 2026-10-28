using System.Text.RegularExpressions;
using StickerSnap.Core.Errors;

namespace StickerSnap.Core.Links;

/// <summary>The sites a video is imported from by link (contract, LinkSite).</summary>
public enum LinkSite
{
    Youtube,
    Giphy,
}

/// <summary>
/// A link to one video of a site we import from, told by the look of what was pasted (artboard
/// 20): `?t=`, a playlist around the video and the tracking parameters are dropped, so the same
/// video always has the same <see cref="Url"/>. Whether the video is there is the site's to say.
/// </summary>
/// <param name="VideoId">The site's id of the video: `dQw4w9WgXcQ`, `JIX9t2j0ZTN9S`</param>
/// <param name="Url">The link to the one video, as the contract's LinkVideo.url gives it</param>
/// <param name="IsSticker">A Giphy sticker: it comes as WEBP, which keeps its transparency</param>
public sealed partial record VideoLink(LinkSite Site, string VideoId, string Url, bool IsSticker = false)
{
    private static readonly HashSet<string> YoutubeHosts =
        ["youtube.com", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com", "youtu.be"];

    /// <summary>The link in what was pasted, with https:// added when it was copied without it.</summary>
    /// <exception cref="AppException">
    /// link_unsupported (`params.reason`: not_link for text that is no link, site for a site we do
    /// not take or a page of it that is not a video); video_unavailable (`params.reason`: playlist)
    /// for a playlist without a video in it
    /// </exception>
    public static VideoLink Parse(string? text)
    {
        var uri = UriOf(text) ?? throw Unsupported("not_link", text);
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];

        var link = YoutubeHosts.Contains(host) ? Youtube(uri, host) : Giphy(uri, host);
        return link ?? throw Unsupported("site", text);
    }

    private static Uri? UriOf(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;
        var withScheme = Scheme().IsMatch(trimmed) ? trimmed : $"https://{trimmed}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        // A word without a dot is not a site: «котик» is not a link
        return uri.Host.Contains('.') ? uri : null;
    }

    private static VideoLink? Youtube(Uri uri, string host)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var id = host == "youtu.be"
            ? segments.FirstOrDefault()
            : segments switch
            {
                ["watch", ..] => QueryOf(uri, "v"),
                ["shorts" or "live" or "embed" or "v", var video, ..] => video,
                _ => null,
            };

        if (id is null || !YoutubeId().IsMatch(id))
        {
            // A playlist by itself has no one video to take
            if (QueryOf(uri, "list") is not null || segments is ["playlist", ..])
                throw new AppException(ErrorCode.VideoUnavailable, $"{uri} is a playlist, not a video")
                {
                    Params = new Dictionary<string, object> { ["reason"] = "playlist" },
                };
            return null;
        }
        return new VideoLink(LinkSite.Youtube, id, $"https://www.youtube.com/watch?v={id}");
    }

    /// <summary>
    /// giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S, giphy.com/stickers/…, giphy.com/clips/…, and the
    /// files: media2.giphy.com/media/[v1.…/]JIX9t2j0ZTN9S/giphy.gif, i.giphy.com/JIX9t2j0ZTN9S.webp.
    /// A file does not say whether it is a sticker, so it is taken as a gif.
    /// </summary>
    private static VideoLink? Giphy(Uri uri, string host)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id;
        var sticker = false;
        if (host == "giphy.com")
        {
            if (segments is not [var kind and ("gifs" or "stickers" or "clips"), var slug, ..]) return null;
            id = slug.Split('-')[^1];
            sticker = kind == "stickers";
        }
        else if (host == "i.giphy.com" || MediaHost().IsMatch(host))
        {
            id = segments switch
            {
                ["media", var media, _, ..] when media.StartsWith("v1.", StringComparison.Ordinal) => segments[2],
                ["media", var media, ..] => media,
                [var file] => Path.GetFileNameWithoutExtension(file),
                _ => null,
            };
        }
        else
        {
            return null;
        }

        if (id is null || !GiphyId().IsMatch(id)) return null;
        return new VideoLink(LinkSite.Giphy, id, $"https://giphy.com/{(sticker ? "stickers" : "gifs")}/{id}", sticker);
    }

    private static string? QueryOf(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf('=');
            if (at > 0 && pair[..at] == name) return Uri.UnescapeDataString(pair[(at + 1)..]);
        }
        return null;
    }

    private static AppException Unsupported(string reason, string? text) =>
        new(ErrorCode.LinkUnsupported, reason == "not_link" ? "The text is not a link" : $"{text} is not a video of YouTube or Giphy")
        {
            Params = new Dictionary<string, object> { ["reason"] = reason },
        };

    [GeneratedRegex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YoutubeId();

    [GeneratedRegex("^[A-Za-z0-9]{5,40}$")]
    private static partial Regex GiphyId();

    [GeneratedRegex(@"^media\d?\.giphy\.com$")]
    private static partial Regex MediaHost();
}
