using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// The server's own spans and metrics, under one name. Only the web server listens to them and
/// sends them on (Api/Observability); in the desktop app nothing listens, so they cost nothing and
/// nothing leaves the user's computer.
/// </summary>
public static class Telemetry
{
    public const string Name = "StickerSnap";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>How long a background job ran, by kind (preview, export, publish) and how it ended.</summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "stickersnap.job.duration", unit: "s", description: "Background jobs by kind and outcome");
}
