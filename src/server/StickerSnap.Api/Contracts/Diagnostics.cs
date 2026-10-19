using StickerSnap.Application.Diagnostics;

namespace StickerSnap.Api.Contracts;

/// <summary>GET /api/v1/diagnostics (schema DiagnosticsReport): what goes with a bug report.</summary>
/// <param name="Text">The report, up to 200 KB in UTF-8</param>
/// <param name="Bytes">Its size in UTF-8, for «≈ 34 КБ»</param>
public sealed record DiagnosticsReportBody(string Text, int Bytes)
{
    public static DiagnosticsReportBody From(DiagnosticsReport report) => new(report.Text, report.Bytes);
}
