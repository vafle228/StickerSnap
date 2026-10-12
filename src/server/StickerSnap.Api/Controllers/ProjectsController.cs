using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.App;
using StickerSnap.Application.Projects;

namespace StickerSnap.Api.Controllers;

/// <summary>
/// /api/v1/projects of group projects: the list, opening, renaming, removing and the autosave.
/// JSON is named per action: DELETE answers 204 without a body.
/// </summary>
[ApiController]
[Route("api/v1/projects")]
[Tags("projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _service;
    private readonly IHostInfo _host;

    public ProjectsController(IProjectService service, IHostInfo host)
    {
        _service = service;
        _host = host;
    }

    /// <summary>The recent projects, newest first; the start screen takes the first three.</summary>
    [HttpGet(Name = "listProjects")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectSummary>), StatusCodes.Status200OK)]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var projects = await _service.ListAsync(cancellationToken);
        return Ok(projects.Select(ProjectSummary.From).ToList());
    }

    /// <summary>
    /// Reads the video and makes a project of it with one segment; sends projectChanged. Dropping
    /// the request while the video is read is «Отменить» and creates nothing.
    /// </summary>
    [HttpPost(Name = "createProject")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Create([FromBody] SourceRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request.Source.ToSource(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, Project.From(created, _host.Mode));
    }

    /// <summary>
    /// «Указать новое видео» for «Исходник не найден»: the scene stays, the segments past the new
    /// video's end are named in the answer. Sends projectChanged.
    /// </summary>
    [HttpPut("{id:guid}/source", Name = "replaceProjectSource")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReplaceSourceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> ReplaceSource(Guid id, [FromBody] SourceRequest request, CancellationToken cancellationToken)
    {
        var replaced = await _service.ReplaceSourceAsync(id, request.Source.ToSource(), cancellationToken);
        return Ok(ReplaceSourceResponse.From(replaced, _host.Mode));
    }

    /// <summary>Opens a .ssproj by its path and puts it on the list; also «Вернуть» after «Убрать из списка».</summary>
    [HttpPost("open", Name = "openProject")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Open([FromBody] OpenProjectRequest request, CancellationToken cancellationToken)
    {
        var opened = await _service.OpenAsync(request.Path, cancellationToken);
        return Ok(Project.From(opened, _host.Mode));
    }

    /// <summary>The whole project: the source, the scene, the last export of each segment.</summary>
    [HttpGet("{id:guid}", Name = "getProject")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Project), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var opened = await _service.GetAsync(id, cancellationToken);
        return Ok(Project.From(opened, _host.Mode));
    }

    /// <summary>Renames the project (F2) inside its file; sends projectChanged.</summary>
    [HttpPatch("{id:guid}", Name = "renameProject")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProjectSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await _service.RenameAsync(id, request.Name, cancellationToken);
        return Ok(ProjectSummary.From(project));
    }

    /// <summary>
    /// `forget` takes the project off the list and leaves its file; `trash` sends the .ssproj to the
    /// trash, never the source or the stickers. Sends projectRemoved.
    /// </summary>
    [HttpDelete("{id:guid}", Name = "deleteProject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> Delete(Guid id, [FromQuery, Required] RemoveMode? mode, CancellationToken cancellationToken)
    {
        await _service.RemoveAsync(id, mode!.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>The editor's autosave: the whole scene over the revision it last saw; another revision is 409.</summary>
    [HttpPut("{id:guid}/scene", Name = "saveScene")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SaveSceneResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status507InsufficientStorage, "application/problem+json")]
    [ProducesDefaultResponseType(typeof(ProblemDetails))]
    public async Task<IActionResult> SaveScene(Guid id, [FromBody] SaveSceneRequest request, CancellationToken cancellationToken)
    {
        var saved = await _service.SaveSceneAsync(id, request.Revision, request.Scene.ToScene(), cancellationToken);
        return Ok(SaveSceneResponse.From(saved));
    }
}
