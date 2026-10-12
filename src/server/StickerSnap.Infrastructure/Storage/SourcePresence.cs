using StickerSnap.Application.Projects;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>A source is there when a file is at its path; whether it is the same video is B10's fingerprint.</summary>
internal sealed class SourcePresence : ISourcePresence
{
    public PresenceStatus Of(string path) => File.Exists(path) ? PresenceStatus.Ok : PresenceStatus.Missing;
}
