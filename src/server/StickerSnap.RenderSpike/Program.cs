using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StickerSnap.Application;
using StickerSnap.Application.Render;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure;

namespace StickerSnap.RenderSpike;

/// <summary>
/// `dotnet run --project StickerSnap.RenderSpike -- &lt;corpus folder&gt; &lt;output folder&gt; [--crf 31]`:
/// renders the default sticker of every clip in the corpus four ways (CRF, two passes, a CRF
/// draft, the fit ladder) and writes report.md and report.csv beside the stickers. FFmpeg is found as the
/// server finds it: StickerSnap__FfmpegDir, else next to the program, else on PATH.
/// </summary>
public sealed class Program
{
    private const int DefaultCrf = 31;

    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out var corpus, out var output, out var crf))
        {
            Console.Error.WriteLine("Usage: StickerSnap.RenderSpike <corpus folder> <output folder> [--crf 31]");
            return 2;
        }

        var sources = Directory.EnumerateFiles(corpus)
            .Where(path => MediaFormats.IsSupportedExtension(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sources.Count == 0)
        {
            Console.Error.WriteLine($"No mp4, mov, webm, gif or webp in {corpus}");
            return 1;
        }

        using var host = Build();
        var service = host.Services.GetRequiredService<CorpusRenderService>();
        RenderMode[] modes =
        [
            new("crf", RenderEffort.Final, crf),
            new("2pass", RenderEffort.Final, null),
            new("draft", RenderEffort.Draft, crf),
            new("fit", RenderEffort.Final, null, Fit: true),
        ];

        Directory.CreateDirectory(output);
        Console.WriteLine($"Rendering {sources.Count} clips from {corpus} into {output}");
        var report = await service.RunAsync(sources, output, modes, new ConsoleProgress(), CancellationToken.None);

        var markdown = Path.Combine(output, "report.md");
        await File.WriteAllTextAsync(markdown, ReportWriter.Markdown(report));
        await File.WriteAllTextAsync(Path.Combine(output, "report.csv"), ReportWriter.Csv(report));
        Console.WriteLine($"Report: {markdown}");
        return 0;
    }

    private static IHost Build()
    {
        // Configuration from the environment only: the arguments are the corpus, not settings
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        return builder.Build();
    }

    private static bool TryParse(string[] args, out string corpus, out string output, out int crf)
    {
        corpus = output = "";
        crf = DefaultCrf;
        List<string> positional = [];
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--crf" && i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out crf)) i++;
            else if (args[i].StartsWith("--", StringComparison.Ordinal)) return false;
            else positional.Add(args[i]);
        }

        if (positional.Count != 2 || !Directory.Exists(positional[0])) return false;
        (corpus, output) = (positional[0], positional[1]);
        return true;
    }

    /// <summary>A line per clip as it is done: the corpus takes minutes.</summary>
    private sealed class ConsoleProgress : IProgress<CorpusEntry>
    {
        public void Report(CorpusEntry value) => Console.WriteLine(ReportWriter.Line(value));
    }
}
