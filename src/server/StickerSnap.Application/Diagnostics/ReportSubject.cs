namespace StickerSnap.Application.Diagnostics;

/// <summary>What failed (schema ErrorSource).</summary>
public enum ErrorSource
{
    /// <summary>A request or a job of the server: the journal knows it by its trace id</summary>
    Server,

    /// <summary>The interface itself: a screen that fell, an error nothing caught</summary>
    Interface,

    /// <summary>The last launch of the app ended in a crash, as the shell found it</summary>
    Shell,
}

/// <summary>
/// What a report is about: the error the window was opened from, as the interface saw it, and
/// what the user did before it (artboard 19·4б). An empty subject is a report from the menu.
/// </summary>
/// <param name="TraceId">The server's trace id; for an error of the interface or the shell, the number the interface gave it</param>
/// <param name="Code">The code the interface showed, render_failed</param>
/// <param name="Message">What the interface or the shell knows of the error the server does not: the exception's text, why it stopped</param>
/// <param name="Stack">The stack and the component stack, or the tail of the server's output for the shell</param>
/// <param name="At">When it first happened</param>
/// <param name="Count">How many times it happened in 10 minutes</param>
/// <param name="Actions">The interface's last steps, the oldest first</param>
public sealed record ReportSubject(
    string? TraceId = null,
    string? Code = null,
    ErrorSource Source = ErrorSource.Server,
    string? Message = null,
    string? Stack = null,
    DateTimeOffset? At = null,
    int Count = 1,
    IReadOnlyList<InterfaceAction>? Actions = null)
{
    /// <summary>A report that knows of no error: GET /api/v1/diagnostics without a trace.</summary>
    public static ReportSubject None { get; } = new();
}

/// <param name="What">«Раздел: Проекты», «Кнопка: Экспорт», «POST /api/v1/exports → 202»; never what a field holds</param>
public sealed record InterfaceAction(DateTimeOffset At, string What);
