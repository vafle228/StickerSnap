namespace StickerSnap.Core.Media;

/// <summary>
/// Where a file the SPA hands over is (contract, SourceRef): the desktop app gives a path on disk,
/// a browser first uploads the file and gives the upload's id.
/// </summary>
public abstract record SourceRef
{
    private SourceRef()
    {
    }

    /// <summary>`kind: path`, only from the desktop app.</summary>
    public sealed record OnDisk(string Path) : SourceRef;

    /// <summary>`kind: upload`, after POST /api/v1/uploads.</summary>
    public sealed record Upload(string UploadId) : SourceRef;
}
