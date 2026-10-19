namespace StickerSnap.Application.Diagnostics;

/// <summary>What the app is and runs on, for the head of a report, and what of the user's must not leave it.</summary>
public interface ISystemSummary
{
    /// <returns>Lines such as «StickerSnap 0.3.1 (desktop)», «FFmpeg 9.0.2 (ok)», «Windows 11 (x64)»</returns>
    Task<IReadOnlyList<string>> DescribeAsync(CancellationToken cancellationToken);

    /// <summary>The names of the system's user, which a report shows as &lt;user&gt;.</summary>
    IReadOnlyList<string> UserNames { get; }

    /// <returns>Keys this computer holds, which a report shows as &lt;secret&gt; wherever they turn up</returns>
    Task<IReadOnlyList<string>> SecretsAsync(CancellationToken cancellationToken);
}
