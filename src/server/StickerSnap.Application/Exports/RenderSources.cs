using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Application.Exports;

/// <summary>What a preview and an export need before they render: the project, with its video in place.</summary>
internal static class RenderSources
{
    /// <exception cref="AppException">
    /// project_not_found, project_invalid, project_too_new; source_not_found when the video is gone
    /// </exception>
    public static async Task<OpenedProject> ProjectWithSourceAsync(IProjectService projects, Guid id, CancellationToken cancellationToken)
    {
        var opened = await projects.GetAsync(id, cancellationToken);
        if (opened.SourceStatus == PresenceStatus.Missing)
            throw new AppException(ErrorCode.SourceNotFound, $"The video of project {id} is not at {opened.Project.Source.Path}");
        return opened;
    }
}
