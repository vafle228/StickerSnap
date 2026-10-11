namespace StickerSnap.Api.Shell;

/// <summary>
/// What the desktop shell (src/desktop) tells the server it starts, as StickerSnap__* variables.
/// Run by hand or in the browser of stage 1a, the server gets none of them.
/// </summary>
public sealed class ShellOptions
{
    public const string Section = "StickerSnap";

    /// <summary>
    /// The one-time token of this launch (contract, «Доступ»). With it, /api and /hubs answer 401
    /// to a request that does not carry it; without it they stay open, as in stage 1a.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>desktop when the shell runs the server, web otherwise; GET /api/v1/app reports it.</summary>
    public string Mode { get; set; } = "web";

    /// <summary>The shell's process: the server quits once it is gone, even after a crash.</summary>
    public int? ParentPid { get; set; }
}
