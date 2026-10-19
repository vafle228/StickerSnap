using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Application.App;
using StickerSnap.Application.Media;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Infrastructure.Diagnostics;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Diagnostics;

/// <summary>The head of a report: versions, mode and system; and the user's name and key it must hide.</summary>
public sealed class HostSummaryTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-summary-");
    private readonly DeviceCredentials _credentials;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public HostSummaryTests()
    {
        _credentials = new DeviceCredentials(
            Options.Create(new DataOptions { DataDir = _data.FullName }), NullLogger<DeviceCredentials>.Instance);
    }

    public void Dispose()
    {
        _credentials.Dispose();
        _data.Delete(recursive: true);
    }

    private HostSummary Summary()
    {
        var host = Substitute.For<IHostInfo>();
        host.AppVersion.Returns("0.3.1");
        host.Mode.Returns(AppMode.Desktop);
        var engine = Substitute.For<IMediaEngine>();
        engine.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(new MediaEngineInfo("FFmpeg", MediaEngineStatus.Ok, "9.0.2"));
        return new HostSummary(host, engine, new ThisComputer(host), _credentials);
    }

    [Fact]
    public async Task Names_the_versions_the_mode_and_the_system()
    {
        var lines = await Summary().DescribeAsync(Cancel);

        lines[0].Should().Be("StickerSnap 0.3.1 (desktop)");
        lines[1].Should().Be("FFmpeg 9.0.2 (ok)");
        lines[2].Should().StartWith(".NET");
        lines.Should().HaveCount(4);
    }

    [Fact]
    public async Task Leaves_out_the_names_of_the_user_and_the_computer()
    {
        var lines = await Summary().DescribeAsync(Cancel);

        string.Join('\n', lines).Should().NotContain(Environment.UserName).And.NotContain(Environment.MachineName);
    }

    [Fact]
    public void Knows_the_user_s_name_to_hide()
    {
        Summary().UserNames.Should().Contain(Environment.UserName);
    }

    [Fact]
    public async Task Hides_the_device_key_once_the_computer_is_linked()
    {
        (await Summary().SecretsAsync(Cancel)).Should().BeEmpty();

        await _credentials.SaveKeyAsync("dk_8f2b5c9e1a7d4f3b", Cancel);

        (await Summary().SecretsAsync(Cancel)).Should().Equal("dk_8f2b5c9e1a7d4f3b");
    }
}
