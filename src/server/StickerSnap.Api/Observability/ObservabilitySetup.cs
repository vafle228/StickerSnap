using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StickerSnap.Api.Shell;
using StickerSnap.Application.Diagnostics;

namespace StickerSnap.Api.Observability;

/// <summary>
/// Traces, metrics and logs of the web server by OpenTelemetry, sent over OTLP to the collector
/// in OTEL_EXPORTER_OTLP_ENDPOINT (Alloy beside the services on the VPS, docker/telegram/observability).
/// The desktop app never sends anything: in desktop mode this is off whatever the environment
/// says, and the server does not even pass a trace id on to the cloud service. Its own log for
/// bug reports (Infrastructure/Diagnostics/FileLog) stays as it is in both modes.
/// </summary>
public static class ObservabilitySetup
{
    public const string ServiceName = "stickersnap-server";

    /// <summary>The OTLP endpoint is set and the shell did not start this server.</summary>
    public static bool IsOn(IConfiguration configuration) =>
        !IsDesktop(configuration) &&
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        if (IsDesktop(configuration))
        {
            // No traceparent leaves the user's computer: the cloud service starts its own traces.
            DistributedContextPropagator.Current = DistributedContextPropagator.CreateNoOutputPropagator();
            return services;
        }
        if (!IsOn(configuration)) return services;

        var version = ShellHostInfo.VersionOf(typeof(ObservabilitySetup).Assembly);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } name ? name : ServiceName,
                serviceVersion: version))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime"))
            .WithLogging(configureBuilder: null, configureOptions: options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            })
            // OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_PROTOCOL and the rest come from the environment
            .UseOtlpExporter();
        return services;
    }

    private static bool IsDesktop(IConfiguration configuration) =>
        string.Equals(configuration[$"{ShellOptions.Section}:Mode"], "desktop", StringComparison.OrdinalIgnoreCase);
}
