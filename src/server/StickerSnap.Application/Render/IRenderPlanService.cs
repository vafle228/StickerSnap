using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Render;

/// <summary>What rendering the stickers of a project would run, without running it.</summary>
public interface IRenderPlanService
{
    /// <exception cref="AppException">As <see cref="IProjectStore.ReadAsync"/></exception>
    Task<IReadOnlyList<SegmentPlan>> DescribeAsync(string projectPath, CancellationToken cancellationToken);
}
