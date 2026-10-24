using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Api.Observability;

namespace StickerSnap.Tests.Api.Observability;

/// <summary>Telemetry leaves only a web server with a collector, never the desktop app.</summary>
public sealed class ObservabilitySetupTests
{
    private static IConfiguration Settings(string? mode, string? endpoint) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StickerSnap:Mode"] = mode,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
        })
        .Build();

    [Theory]
    [InlineData("web", "http://alloy:4317", true)]
    [InlineData(null, "http://alloy:4317", true)]
    [InlineData("desktop", "http://alloy:4317", false)]
    [InlineData("Desktop", "http://alloy:4317", false)]
    [InlineData("web", null, false)]
    [InlineData("web", " ", false)]
    public void Is_on_only_for_a_web_server_with_a_collector(string? mode, string? endpoint, bool on)
    {
        ObservabilitySetup.IsOn(Settings(mode, endpoint)).Should().Be(on);
    }

    [Fact]
    public void Adds_nothing_to_the_desktop_app_whatever_the_environment_says()
    {
        var services = new ServiceCollection().AddObservability(Settings("desktop", "http://alloy:4317"));

        services.Should().BeEmpty();
    }

    [Fact]
    public void Adds_open_telemetry_to_a_web_server_with_a_collector()
    {
        var services = new ServiceCollection().AddObservability(Settings("web", "http://alloy:4317"));

        services.Should().NotBeEmpty();
    }
}
