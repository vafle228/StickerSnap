using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Projects;

/// <summary>A card of the «Проекты» grid as the list and projectChanged give it.</summary>
public sealed record ProjectListItem(ProjectCard Card, PresenceStatus SourceStatus);

/// <summary>A project read from its file, with where the file is and whether its source is there.</summary>
public sealed record OpenedProject(Project Project, string Path, PresenceStatus SourceStatus);

/// <summary>What PUT …/scene answers: the revision the SPA sends with its next save.</summary>
public sealed record SavedScene(int Revision, DateTimeOffset SavedAt);

/// <summary>DELETE /projects/{id}: «Убрать из списка» or «Удалить с диска».</summary>
public enum RemoveMode
{
    /// <summary>Off the list; the file stays and «Вернуть» opens it again.</summary>
    Forget,

    /// <summary>The .ssproj to the trash; the source and the exported stickers stay.</summary>
    Trash,
}
