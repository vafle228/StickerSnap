using System.Globalization;
using System.Text;
using StickerSnap.Application.Render;
using StickerSnap.Core.Media;

namespace StickerSnap.RenderSpike;

/// <summary>The spike's report: a summary per mode and a row per clip, as Markdown and as CSV.</summary>
internal static class ReportWriter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Line(CorpusEntry entry)
    {
        var name = Path.GetFileName(entry.SourcePath);
        if (entry.Failure is { } failure) return $"{name}: {failure.Message}";
        return $"{name}: " + string.Join(", ", entry.Outcomes.Select(o => o.Failure is null
            ? string.Create(Invariant, $"{o.Mode.Name} {Kb(o.SizeBytes)} KB in {o.Elapsed.TotalSeconds:0.0} s{(o.Passes ? "" : " FAILS")}")
            : $"{o.Mode.Name} failed"));
    }

    public static string Markdown(CorpusReport report)
    {
        var limits = report.Limits;
        var rendered = report.Entries.Where(e => e.Failure is null).ToList();
        var text = new StringBuilder();
        text.AppendLine("# Render spike report").AppendLine();
        text.AppendLine(Invariant,
            $"{report.Entries.Count} clips, {rendered.Count} read. The default sticker: the first {limits.MaxDurationSec} s, the middle square, {limits.Side} px, {limits.MaxFps} fps, VP9 in WEBM; limit {limits.MaxBytes / 1024} KB.");
        text.AppendLine();

        text.AppendLine("| Mode | Rate | Effort | Pass | Size median / max, KB | Time mean / max, s |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var mode in report.Modes)
        {
            var outcomes = rendered.SelectMany(e => e.Outcomes).Where(o => o.Mode == mode).ToList();
            var sizes = outcomes.Where(o => o.SizeBytes is not null).Select(o => o.SizeBytes!.Value).Order().ToList();
            var times = outcomes.Select(o => o.Elapsed.TotalSeconds).ToList();
            var passed = outcomes.Count(o => o.Passes);
            var rate = mode.Fit ? "fit ladder, then 2-pass" : mode.Crf is { } crf ? $"crf {crf}" : "2-pass, 0.95·256 KB·8 / t";
            var size = sizes.Count == 0 ? "—" : $"{Kb(sizes[sizes.Count / 2])} / {Kb(sizes[^1])}";
            var time = times.Count == 0 ? "—" : string.Create(Invariant, $"{times.Average():0.0} / {times.Max():0.0}");
            text.AppendLine(Invariant, $"| {mode.Name} | {rate} | {mode.Effort} | {passed} / {rendered.Count} ({Percent(passed, rendered.Count)}) | {size} | {time} |");
        }

        var transparent = rendered.Where(e => e.Source!.HasAlpha).ToList();
        if (transparent.Count > 0)
        {
            var kept = report.Modes.Select(m =>
                $"{m.Name} {transparent.Count(e => e.Outcomes.Single(o => o.Mode == m).KeptAlpha(e.Source!))}");
            text.AppendLine().AppendLine(Invariant, $"Transparent sources: {transparent.Count}; alpha kept: {string.Join(", ", kept)}.");
        }

        foreach (var mode in report.Modes.Where(m => m.Fit)) Ladder(text, mode, rendered);

        text.AppendLine().AppendLine("## Clips").AppendLine();
        text.Append("| Clip | Source |");
        foreach (var mode in report.Modes) text.Append(Invariant, $" {mode.Name} |");
        text.AppendLine();
        text.Append("| --- | --- |").AppendLine(string.Concat(report.Modes.Select(_ => " --- |")));
        foreach (var entry in report.Entries)
        {
            var source = entry.Source is { } s ? Describe(s) : entry.Failure!.Message;
            text.Append(Invariant, $"| {Path.GetFileName(entry.SourcePath)} | {source} |");
            foreach (var mode in report.Modes)
            {
                var outcome = entry.Outcomes.FirstOrDefault(o => o.Mode == mode);
                text.Append(' ').Append(outcome is null ? "—" : Cell(outcome, entry.Source!)).Append(" |");
            }

            text.AppendLine();
        }

        var failures = report.Entries
            .SelectMany(e => e.Outcomes.Where(o => o.Failure?.Log is not null).Select(o => (e.SourcePath, o.Mode.Name, o.Failure!.Log)))
            .ToList();
        if (failures.Count > 0)
        {
            text.AppendLine().AppendLine("## Engine logs of failed renders").AppendLine();
            foreach (var (source, mode, log) in failures)
            {
                text.AppendLine(Invariant, $"### {Path.GetFileName(source)}, {mode}").AppendLine();
                text.AppendLine("```").AppendLine(log).AppendLine("```").AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>Which step of the ladder each clip took, and how it got there.</summary>
    private static void Ladder(StringBuilder text, RenderMode mode, IReadOnlyList<CorpusEntry> rendered)
    {
        var fitted = rendered
            .Select(e => (Entry: e, Fit: e.Outcomes.Single(o => o.Mode == mode).Fitted))
            .Where(x => x.Fit is not null)
            .ToList();
        text.AppendLine().AppendLine(Invariant, $"## Fit ladder ({mode.Name})").AppendLine();
        text.AppendLine("| Step taken | Clips | At its CRF | Two-pass renders |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var group in fitted.GroupBy(x => x.Fit!.Rung.ToString()).OrderBy(g => g.Min(x => x.Fit!.Steps.Count)))
        {
            var renders = group.Select(x => x.Fit!.Attempts.Count).ToList();
            text.AppendLine(Invariant,
                $"| {group.Key} | {group.Count()} | {group.Count(x => x.Fit!.HoldsCrf)} | {renders.Count(r => r == 1)} × 1, {renders.Count(r => r > 1)} × more |");
        }

        text.AppendLine().AppendLine("| Clip | Steps estimated, KB | Two-pass renders, kbit/s → KB | Gave up |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var (entry, fit) in fitted)
        {
            var steps = string.Join(", ", fit!.Steps.Select(s => $"{s.Rung.Fps}/{s.Rung.Crf}{(s.Rung.Detail.LongerSide == entry.Spec!.Size.LongerSide ? "" : $"/{s.Rung.Detail.LongerSide}")} {Kb(s.EstimatedBytes)}"));
            var attempts = string.Join(", ", fit.Attempts.Select(a => $"{a.Rate.BitsPerSecond / 1000} → {Kb(a.Bytes)}"));
            var gave = fit.Concessions.Count == 0 ? "—" : string.Join(", ", fit.Concessions);
            text.AppendLine(Invariant, $"| {Path.GetFileName(entry.SourcePath)} | {steps} | {attempts} | {gave}{(fit.HoldsCrf ? "" : ", below its CRF")} |");
        }
    }

    public static string Csv(CorpusReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "clip,format,codec,width,height,fps,vfr,duration_s,alpha,mode,rate,effort,size_bytes,time_s,out_width,out_height,out_fps,out_duration_s,out_alpha,passes,problems,error,fit_crf,fit_concessions,fit_steps,fit_renders");
        foreach (var entry in report.Entries)
        {
            var clip = Quote(Path.GetFileName(entry.SourcePath));
            var s = entry.Source;
            var source = s is null
                ? ",,,,,,,"
                : string.Join(',', s.Format, s.Codec, Number(s.Width), Number(s.Height), s.Fps, s.IsVariableFrameRate, Number(s.DurationSec), s.HasAlpha);
            if (entry.Outcomes.Count == 0)
            {
                text.AppendLine($"{clip},{source},,,,,,,,,,,,{Quote(entry.Failure?.Message)},,,,");
                continue;
            }

            foreach (var o in entry.Outcomes)
            {
                var t = o.Sticker;
                text.AppendLine(string.Join(',',
                    clip, source, o.Mode.Name, Quote(o.Rate.ToString()), o.Mode.Effort,
                    o.SizeBytes is { } bytes ? Number(bytes) : "", Number(o.Elapsed.TotalSeconds),
                    t is null ? "" : Number(t.Width), t is null ? "" : Number(t.Height), t?.Fps.ToString() ?? "",
                    t is null ? "" : Number(t.DurationSec), t?.HasAlpha.ToString() ?? "",
                    o.Passes, Quote(string.Join("; ", o.Problems)), Quote(o.Failure?.Message),
                    Fit(o.Fitted)));
            }
        }

        return text.ToString();
    }

    /// <summary>The fit columns: the CRF, the concessions, the steps estimated and the two-pass renders.</summary>
    private static string Fit(FittedSticker? fit) => fit is null
        ? ",,,"
        : string.Join(',', Number(fit.Rung.Crf), Quote(string.Join("; ", fit.Concessions)), Number(fit.Steps.Count), Number(fit.Attempts.Count));

    private static string Describe(MediaInfo s) => string.Create(Invariant,
        $"{s.Format} {s.Codec} {s.Width}×{s.Height}, {s.Fps.ToDouble():0.##}{(s.IsVariableFrameRate ? " VFR" : "")} fps, {s.DurationSec:0.0} s{(s.HasAlpha ? ", alpha" : "")}");

    private static string Cell(ModeOutcome o, MediaInfo source)
    {
        if (o.Failure is not null) return "failed";
        var cell = string.Create(Invariant, $"{Kb(o.SizeBytes)} KB, {o.Elapsed.TotalSeconds:0.0} s");
        if (!o.KeptAlpha(source)) cell += ", alpha lost";
        return o.Problems.Count == 0 ? cell : $"{cell}, **{string.Join("; ", o.Problems)}**";
    }

    private static string Kb(long? bytes) => bytes is { } b ? (b / 1024.0).ToString("0.0", Invariant) : "—";

    private static string Percent(int part, int whole) =>
        whole == 0 ? "—" : (100.0 * part / whole).ToString("0", Invariant) + "%";

    private static string Number(double value) => value.ToString("0.###", Invariant);

    private static string Quote(string? value) => value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
}
