using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Links;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Links;

/// <summary>The probe: the link read first, the site asked once for a probe and the download after it.</summary>
public sealed class LinkServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ControlledSites _sites = new();
    private readonly LinkService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public LinkServiceTests() => _service = new LinkService(_sites, _clock);

    [Fact]
    public async Task Asks_the_site_about_the_one_video_of_the_link()
    {
        var video = await _service.ProbeAsync("youtu.be/dQw4w9WgXcQ?t=42", Cancel);

        video.Url.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        _sites.Probes.Should().Be(1);
    }

    [Fact]
    public async Task Remembers_what_the_site_said_for_a_while()
    {
        await _service.ProbeAsync("https://youtu.be/dQw4w9WgXcQ", Cancel);
        await _service.ProbeAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ", Cancel);
        _sites.Probes.Should().Be(1);

        _clock.Advance(LinkService.Remember);
        await _service.ProbeAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ", Cancel);

        _sites.Probes.Should().Be(2);
    }

    [Fact]
    public async Task Answers_a_bad_link_without_asking_a_site()
    {
        var probe = () => _service.ProbeAsync("https://vimeo.com/1", Cancel);

        (await probe.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.LinkUnsupported);
        _sites.Probes.Should().Be(0);
    }

    [Fact]
    public async Task Does_not_remember_a_failure()
    {
        _sites.Probe = _ => throw new AppException(ErrorCode.SiteUnreachable, "offline");
        var probe = () => _service.ProbeAsync("https://youtu.be/dQw4w9WgXcQ", Cancel);
        await probe.Should().ThrowAsync<AppException>();

        _sites.Probe = link => Videos.Of(link.Url);
        var video = await _service.ProbeAsync("https://youtu.be/dQw4w9WgXcQ", Cancel);

        video.Title.Should().Be("Кот смотрит в окно");
        _sites.Probes.Should().Be(2);
    }
}
