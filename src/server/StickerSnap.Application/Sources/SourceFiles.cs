using StickerSnap.Application.App;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Application.Sources;

/// <summary>The file on this machine that a <see cref="SourceRef"/> names.</summary>
public sealed class SourceFiles(IHostInfo host, IUploadStore uploads)
{
    /// <exception cref="AppException">
    /// `bad_request` for a path from a browser, which has no paths and must not read the server's
    /// disk; `source_not_found` for an upload this server does not keep
    /// </exception>
    public string PathOf(SourceRef source) => source switch
    {
        SourceRef.OnDisk { Path: var path } when host.Mode == AppMode.Desktop => path,
        SourceRef.OnDisk => throw new AppException(ErrorCode.BadRequest, "A path source comes only from the desktop app")
        {
            Fields = new Dictionary<string, string[]> { ["source.kind"] = ["desktop_only"] },
        },
        SourceRef.Upload { UploadId: var id } => uploads.Find(id)
            ?? throw new AppException(ErrorCode.SourceNotFound, $"No upload {id} on this server"),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "an unknown kind of source"),
    };
}
