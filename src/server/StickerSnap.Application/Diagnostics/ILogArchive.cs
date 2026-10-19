namespace StickerSnap.Application.Diagnostics;

/// <summary>The server's own log, as it was written to disk: what a report takes around an error.</summary>
public interface ILogArchive
{
    /// <returns>
    /// The entries written at or after <paramref name="since"/>, oldest first. An entry is its
    /// line, and the lines of an exception's stack after it.
    /// </returns>
    Task<IReadOnlyList<string>> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
