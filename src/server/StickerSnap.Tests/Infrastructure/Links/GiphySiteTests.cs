using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Links;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Core.Media;
using StickerSnap.Infrastructure.Links;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>Giphy's oEmbed and files answered by a stub, the media engine substituted, the cache in a temp directory.</summary>
public sealed class GiphySiteTests : IDisposable
{
    private static readonly byte[] Mp4 = [.. Enumerable.Range(0, 200_000).Select(i => (byte)i)];

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-giphy-");
    private readonly GiphyStub _giphy = new();
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly GiphySite _site;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public GiphySiteTests()
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(GiphySite.ClientName).Returns(_ => new HttpClient(_giphy, disposeHandler: false));
        _probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Media(new FileInfo(call.Arg<string>()).Length));
        _site = new GiphySite(
            clients, _probe, Options.Create(new DataOptions { DataDir = _data.FullName }),
            new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<GiphySite>.Instance);
    }

    public void Dispose() => _data.Delete(recursive: true);

    private static MediaInfo Media(long bytes) =>
        new(MediaFormat.Mp4, "h264", 480, 480, new Rational(25, 1), false, 80, 3.2, false, false, bytes);

    [Fact]
    public async Task Probes_a_gif_by_its_title_and_its_mp4()
    {
        var video = await _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S"), Cancel);

        video.Should().Be(new LinkVideo(
            LinkSite.Giphy, "https://giphy.com/gifs/JIX9t2j0ZTN9S", "Cat Working", 3.2,
            "https://i.giphy.com/media/JIX9t2j0ZTN9S/200_s.gif", Mp4.Length, "MP4", "mp4", "mp4"));
        _giphy.Asked.Should().Contain("https://i.giphy.com/media/JIX9t2j0ZTN9S/giphy.mp4");
    }

    [Fact]
    public async Task Takes_a_sticker_as_webp_to_keep_its_transparency()
    {
        var video = await _site.ProbeAsync(VideoLink.Parse("https://giphy.com/stickers/cat-JIX9t2j0ZTN9S"), Cancel);

        video.Quality.Should().Be("WEBP");
        video.Extension.Should().Be("webp");
        _giphy.Asked.Should().Contain("https://i.giphy.com/media/JIX9t2j0ZTN9S/giphy.webp");
    }

    [Fact]
    public async Task Downloads_the_file_the_probe_kept_without_asking_again()
    {
        var video = await _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/JIX9t2j0ZTN9S"), Cancel);
        var asked = _giphy.Asked.Count;
        var path = Path.Combine(_data.FullName, "cat.mp4");
        var steps = new List<DownloadProgress>();

        var bytes = await _site.DownloadAsync(video, path, steps.Add, Cancel);

        bytes.Should().Be(Mp4.Length);
        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(Mp4);
        steps[^1].ReceivedBytes.Should().Be(Mp4.Length);
        _giphy.Asked.Should().HaveCount(asked);
    }

    [Fact]
    public async Task Downloads_from_giphy_when_the_probed_file_is_gone()
    {
        var video = await _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/JIX9t2j0ZTN9S"), Cancel);
        Directory.Delete(Path.Combine(_data.FullName, "links"), recursive: true);
        var path = Path.Combine(_data.FullName, "cat.mp4");

        await _site.DownloadAsync(video, path, _ => { }, Cancel);

        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(Mp4);
    }

    [Fact]
    public async Task Says_a_gif_giphy_does_not_have_is_gone()
    {
        _giphy.Status = HttpStatusCode.NotFound;

        var probe = () => _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/JIX9t2j0ZTN9S"), Cancel);

        (await probe.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.VideoUnavailable && (string)e.Params["reason"] == "gone");
    }

    [Fact]
    public async Task Says_giphy_did_not_answer_without_a_network()
    {
        _giphy.Offline = true;

        var probe = () => _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/JIX9t2j0ZTN9S"), Cancel);

        (await probe.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.SiteUnreachable && (string)e.Params["site"] == "giphy");
    }

    [Fact]
    public async Task Says_the_site_changed_for_an_answer_it_does_not_expect()
    {
        _giphy.Status = HttpStatusCode.Forbidden;

        var probe = () => _site.ProbeAsync(VideoLink.Parse("https://giphy.com/gifs/JIX9t2j0ZTN9S"), Cancel);

        (await probe.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SiteChanged);
    }

    [Theory]
    [InlineData("Cat Working GIF - Find & Share on GIPHY", "Cat Working")]
    [InlineData("The Rock Comedy GIF by Baywatch Movie - Find & Share on GIPHY", "The Rock Comedy")]
    [InlineData("Dance Party Sticker - Find & Share on GIPHY", "Dance Party")]
    [InlineData("GIF - Find & Share on GIPHY", "Giphy JIX9t2j0ZTN9S")]
    [InlineData(null, "Giphy JIX9t2j0ZTN9S")]
    public void Cleans_giphy_s_title(string? title, string clean)
    {
        GiphySite.CleanTitle(title, "JIX9t2j0ZTN9S").Should().Be(clean);
    }

    /// <summary>oEmbed and i.giphy.com as Giphy answers them, or a status for every request, or no network.</summary>
    private sealed class GiphyStub : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool Offline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Asked) Asked.Add(url);
            if (Offline) throw new HttpRequestException("No such host is known. (giphy.com:443)");
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));

            HttpContent content = url.StartsWith("https://giphy.com/services/oembed", StringComparison.Ordinal)
                ? new StringContent("""{"title": "Cat Working GIF - Find & Share on GIPHY", "type": "photo"}""", Encoding.UTF8, "application/json")
                : new ByteArrayContent(Mp4);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
