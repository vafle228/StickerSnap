using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StickerSnap.Application;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure;

namespace StickerSnap.RenderSpike;

/// <summary>
/// `dotnet run --project StickerSnap.RenderSpike -- &lt;corpus folder&gt; &lt;output folder&gt; [--crf 31]`:
/// renders the default sticker of every clip in the corpus four ways (CRF, two passes, a CRF
/// draft, the fit ladder) and writes report.md and report.csv beside the stickers. FFmpeg is found as the
/// server finds it: StickerSnap__FfmpegDir, else next to the program, else on PATH.
/// <para>
/// `… -- --print-command &lt;file.ssproj&gt;` prints the FFmpeg commands that render each segment
/// of the project at the first step of the fit ladder, to read and run by hand.
/// </para>
/// <para>
/// `… -- --render &lt;file.ssproj&gt; [--draft]` renders each segment of the project with the
/// render engine, as the export would (or as the exact preview with --draft), beside the project.
/// </para>
/// </summary>
public sealed class Program
{
    private const int DefaultCrf = 31;

    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--print-command", var project]) return await PrintCommandsAsync(project);
        if (args is ["--render", var rendered]) return await RenderAsync(rendered, RenderEffort.Final);
        if (args is ["--render", var drafted, "--draft"]) return await RenderAsync(drafted, RenderEffort.Draft);

        if (!TryParse(args, out var corpus, out var output, out var crf))
        {
            Console.Error.WriteLine("Usage: StickerSnap.RenderSpike <corpus folder> <output folder> [--crf 31]");
            Console.Error.WriteLine("       StickerSnap.RenderSpike --print-command <file.ssproj>");
            Console.Error.WriteLine("       StickerSnap.RenderSpike --render <file.ssproj> [--draft]");
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

    private static async Task<int> PrintCommandsAsync(string project)
    {
        using var host = Build();
        IReadOnlyList<SegmentPlan> plans;
        try
        {
            plans = await host.Services.GetRequiredService<RenderPlanService>().DescribeAsync(project, CancellationToken.None);
        }
        catch (AppException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message}");
            return 1;
        }

        foreach (var plan in plans)
        {
            Console.WriteLine($"# {plan.Segment.Name} ({plan.Segment.Id})");
            if (plan.Spec is { } spec)
            {
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"# {spec.Width}x{spec.Height}, {spec.Fps} fps, {spec.PlayedFrames} frames, {spec.DurationSec:0.###} s, " +
                    $"source frames {spec.Clip.Frames.First}-{spec.Clip.Frames.Last}"));
                Console.WriteLine(plan.Commands);
            }
            else
            {
                Console.WriteLine($"# makes no sticker: {plan.Problem}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> RenderAsync(string project, RenderEffort quality)
    {
        using var host = Build();
        IReadOnlyList<SegmentOutcome> outcomes;
        try
        {
            outcomes = await host.Services.GetRequiredService<ProjectRenderService>()
                .RenderAsync(project, quality, new StageProgress(), CancellationToken.None);
        }
        catch (AppException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message}");
            return 1;
        }

        foreach (var outcome in outcomes)
        {
            Console.WriteLine($"# {outcome.SegmentId}");
            if (outcome.Sticker is { } sticker)
            {
                var output = sticker.Output;
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{sticker.Path}: {sticker.Bytes} B, {output.Width}x{output.Height}, {output.Fps} fps, {output.DurationSec:0.###} s, crf {sticker.Crf}"));
                Console.WriteLine($"given up: {(sticker.Concessions.Count == 0 ? "nothing" : string.Join(", ", sticker.Concessions))}");
                Console.WriteLine($"ladder: {string.Join(" | ", sticker.Ladder.Select(step => $"{step.Rung}: {step.Bytes} B"))}");
                if (sticker.Validation is { } validation)
                    Console.WriteLine($"telegram: {validation.Verdict.ToString().ToLowerInvariant()}{string.Concat(validation.Problems.Select(p => $"; {p}"))}");
            }
            else if (outcome.Failure is { } failure)
            {
                Console.WriteLine($"{failure.Code}: {failure.Message}");
                if (failure.Log is not null) Console.WriteLine(failure.Log);
            }

            Console.WriteLine();
        }

        return outcomes.All(outcome => outcome.Sticker is not null) ? 0 : 1;
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

    /// <summary>A line whenever a segment's stage or attempt changes: the engine's progress, readable.</summary>
    private sealed class StageProgress : IProgress<SegmentProgress>
    {
        private string? _last;

        public void Report(SegmentProgress value)
        {
            var (fraction, stage, attempt) = value.Progress;
            var line = $"{value.SegmentId}: {stage.ToString().ToLowerInvariant()}" +
                (attempt is null ? "" : $", attempt {attempt.N}{string.Concat(attempt.Concessions.Select(c => $", {c}"))}");
            if (line == _last) return;
            _last = line;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{fraction,4:P0} {line}"));
        }
    }

    /// <summary>A line per clip as it is done: the corpus takes minutes.</summary>
    private sealed class ConsoleProgress : IProgress<CorpusEntry>
    {
        public void Report(CorpusEntry value) => Console.WriteLine(ReportWriter.Line(value));
    }
}
