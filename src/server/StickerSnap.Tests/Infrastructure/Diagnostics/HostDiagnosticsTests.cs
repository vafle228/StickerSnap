using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.App;
using StickerSnap.Application.Feedback;
using StickerSnap.Application.Media;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Infrastructure.Diagnostics;

namespace StickerSnap.Tests.Infrastructure.Diagnostics;

/// <summary>The report of a bug: versions, mode, system and the error, nothing of the user's.</summary>
public sealed class HostDiagnosticsTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static HostDiagnostics Diagnostics()
    {
        var host = Substitute.For<IHostInfo>();
        host.AppVersion.Returns("0.3.1");
        host.Mode.Returns(AppMode.Desktop);
        var engine = Substitute.For<IMediaEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(new MediaEngineInfo("FFmpeg", MediaEngineStatus.Ok, "9.0.2"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 13, 22, 5, TimeSpan.Zero));
        return new HostDiagnostics(host, engine, new ThisComputer(host), clock);
    }

    [Fact]
    public async Task Names_the_versions_the_mode_and_the_system()
    {
        var report = await Diagnostics().CollectAsync(null, Cancel);

        var lines = report.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("StickerSnap 0.3.1 (desktop)");
        lines[1].Should().Be("FFmpeg 9.0.2 (ok)");
        lines[2].Should().StartWith(".NET");
        lines[4].Should().Be("Sent at 2026-10-19T13:22:05Z");
        report.Should().NotContain("Error:");
    }

    [Fact]
    public async Task Names_the_error_the_window_was_opened_from()
    {
        var report = await Diagnostics().CollectAsync(new FeedbackError("render_failed", "7f3a-c21e"), Cancel);

        report.Should().Contain("Error: render_failed, trace 7f3a-c21e");
    }

    [Fact]
    public async Task Leaves_out_the_names_of_the_user_and_the_computer()
    {
        var report = await Diagnostics().CollectAsync(null, Cancel);

        report.Should().NotContain(Environment.UserName).And.NotContain(Environment.MachineName);
    }
}
