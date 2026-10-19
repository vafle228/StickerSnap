namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// The errors of this run of the server that are worth a report (reportable codes), newest kept:
/// a report finds an error's context by the trace id the SPA was shown.
/// </summary>
public interface IErrorJournal
{
    /// <summary>Keeps the error when its code is reportable, and writes it to the server's log.</summary>
    void Record(ErrorRecord record);

    /// <returns>The errors with this trace id, oldest first; a job may fail in several segments</returns>
    IReadOnlyList<ErrorRecord> Find(string traceId);

    /// <returns>Up to <paramref name="count"/> errors, newest first</returns>
    IReadOnlyList<ErrorRecord> Latest(int count);
}
