using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Diagnostics;

/// <summary>GET /api/v1/diagnostics and the report a bug goes with (artboard 19, section 4).</summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// The report for the developer: the app and the system, the subject's error with its context
    /// and its project's settings, or what the interface saw of its own error; the latest errors,
    /// the interface's last steps and the server's log around it. Paths, the user's name and keys
    /// are taken out; it fits <see cref="DiagnosticsService.LimitBytes"/>.
    /// </summary>
    /// <exception cref="AppException">bad_request with fields.traceId for a trace id over 64 characters</exception>
    Task<DiagnosticsReport> CollectAsync(ReportSubject subject, CancellationToken cancellationToken);
}

/// <param name="Text">Plain text for a person to read, '\n' line endings</param>
/// <param name="Bytes">Its size in UTF-8</param>
public sealed record DiagnosticsReport(string Text, int Bytes);
