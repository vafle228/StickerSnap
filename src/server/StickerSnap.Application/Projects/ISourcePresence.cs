using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>Whether the video a project was cut from is still where the project remembers it.</summary>
public interface ISourcePresence
{
    PresenceStatus Of(string path);
}
