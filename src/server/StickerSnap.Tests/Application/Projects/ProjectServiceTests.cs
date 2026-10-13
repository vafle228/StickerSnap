using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Events;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Projects;

/// <summary>
/// The use cases of «Проекты» over a list and files in memory: what lands in the file and on the
/// list, what is refused, and what the windows are told.
/// </summary>
public sealed class ProjectServiceTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\Users\u\Проекты" : "/home/u/Проекты";
    private static readonly string CatPath = Path.Combine(Root, "cat.ssproj");
    private static readonly Project Cat = SampleProjects.Sample;
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProjectRegistry _registry = new();
    private readonly Dictionary<string, Project> _files = new(StringComparer.Ordinal);
    private readonly IProjectStore _store = Substitute.For<IProjectStore>();
    private readonly ITrash _trash = Substitute.For<ITrash>();
    private readonly ISourcePresence _sources = Substitute.For<ISourcePresence>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly ISourceWatch _watch = Substitute.For<ISourceWatch>();
    private readonly ProjectService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ProjectServiceTests()
    {
        _store.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            _files.TryGetValue(call.Arg<string>(), out var project)
                ? project
                : throw new AppException(ErrorCode.ProjectNotFound, $"No file at {call.Arg<string>()}"));
        _store.WriteAsync(Arg.Any<string>(), Arg.Any<Project>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _files[call.Arg<string>()] = call.Arg<Project>();
                return Task.CompletedTask;
            });
        _sources.Of(Arg.Any<string>()).Returns(PresenceStatus.Ok);
        _service = new ProjectService(
            _registry, _store, _trash, _sources, _events, Imports.Unused(), Substitute.For<ISettingsService>(),
            _watch, new FakeTimeProvider(Now));
    }

    private async Task<OpenedProject> ListedAsync(Project project, string path)
    {
        _files[path] = project;
        var opened = await _service.OpenAsync(path, Cancel);
        _events.ClearReceivedCalls();
        return opened;
    }

    [Fact]
    public async Task Lists_the_newest_first_with_whether_each_source_is_there()
    {
        var older = Cat with
        {
            Id = Guid.NewGuid(), Name = "Старый", SavedAt = Cat.SavedAt.AddDays(-1),
            Source = Cat.Source with { Path = Path.Combine(Root, "old.gif") },
        };
        await _registry.SaveAsync(ProjectCard.Of(older, Path.Combine(Root, "old.ssproj")), Cancel);
        await _registry.SaveAsync(ProjectCard.Of(Cat, CatPath), Cancel);
        _sources.Of(older.Source.Path).Returns(PresenceStatus.Missing);
        _sources.Of(Cat.Source.Path).Returns(PresenceStatus.Ok);

        var list = await _service.ListAsync(Cancel);

        list.Select(item => (item.Card.Name, item.SourceStatus)).Should().Equal(
            ("Кот и клавиатура", PresenceStatus.Ok),
            ("Старый", PresenceStatus.Missing));
    }

    [Fact]
    public async Task Opens_a_file_puts_it_on_the_list_and_announces_it()
    {
        _files[CatPath] = Cat;

        var opened = await _service.OpenAsync(CatPath, Cancel);

        opened.Should().Be(new OpenedProject(Cat, CatPath, PresenceStatus.Ok));
        _registry.Cards.Should().Equal(ProjectCard.Of(Cat, CatPath));
        await _events.Received(1).ProjectChangedAsync(
            new ProjectListItem(ProjectCard.Of(Cat, CatPath), PresenceStatus.Ok), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("cat.ssproj", "not_absolute")]
    public async Task Refuses_a_path_that_is_not_absolute(string path, string rule)
    {
        var open = () => _service.OpenAsync(path, Cancel);

        var error = (await open.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("path").WhoseValue.Should().Equal(rule);
    }

    [Fact]
    public async Task Lists_a_moved_project_at_the_path_opened_last()
    {
        await ListedAsync(Cat, CatPath);
        var moved = Path.Combine(Root, "Архив", "cat.ssproj");
        _files[moved] = Cat;

        await _service.OpenAsync(moved, Cancel);

        _registry.Cards.Should().ContainSingle().Which.Path.Should().Be(moved);
        await _events.DidNotReceiveWithAnyArgs().ProjectRemovedAsync(default, Cancel);
    }

    [Fact]
    public async Task Drops_the_card_of_the_project_a_path_held_before()
    {
        await ListedAsync(Cat, CatPath);
        var other = Cat with { Id = Guid.NewGuid(), Name = "Другой" };
        _files[CatPath] = other;

        await _service.OpenAsync(CatPath, Cancel);

        _registry.Cards.Should().ContainSingle().Which.Id.Should().Be(other.Id);
        await _events.Received(1).ProjectRemovedAsync(Cat.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gives_the_project_its_file_holds_now_and_refreshes_its_card()
    {
        await ListedAsync(Cat, CatPath);
        var edited = Cat with { Name = "Правили руками" };
        _files[CatPath] = edited;

        var opened = await _service.GetAsync(Cat.Id, Cancel);

        opened.Project.Should().Be(edited);
        _registry.Cards.Single().Name.Should().Be("Правили руками");
        await _events.Received(1).ProjectChangedAsync(Arg.Any<ProjectListItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gives_the_project_without_an_event_when_its_card_is_current()
    {
        await ListedAsync(Cat, CatPath);

        await _service.GetAsync(Cat.Id, Cancel);

        await _events.DidNotReceiveWithAnyArgs().ProjectChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Does_not_find_a_project_off_the_list()
    {
        var get = () => _service.GetAsync(Guid.NewGuid(), Cancel);

        (await get.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectNotFound);
    }

    [Fact]
    public async Task Tells_the_windows_when_the_missing_source_of_an_opened_project_comes_back()
    {
        await ListedAsync(Cat, CatPath);
        Func<Task>? appeared = null;
        _watch.Watch(Cat.Id, Cat.Source.Path, Arg.Do<Func<Task>>(callback => appeared = callback));
        _sources.Of(Cat.Source.Path).Returns(PresenceStatus.Missing);

        var opened = await _service.GetAsync(Cat.Id, Cancel);
        _sources.Of(Cat.Source.Path).Returns(PresenceStatus.Ok);
        appeared.Should().NotBeNull();
        await appeared!();

        opened.SourceStatus.Should().Be(PresenceStatus.Missing);
        await _events.Received(1).ProjectChangedAsync(
            Arg.Is<ProjectListItem>(item => item.Card.Id == Cat.Id && item.SourceStatus == PresenceStatus.Ok),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Watches_no_source_that_is_there()
    {
        await ListedAsync(Cat, CatPath);

        await _service.GetAsync(Cat.Id, Cancel);

        _watch.DidNotReceiveWithAnyArgs().Watch(default, default!, default!);
    }

    [Fact]
    public async Task Stops_watching_the_source_of_a_removed_project()
    {
        await ListedAsync(Cat, CatPath);

        await _service.RemoveAsync(Cat.Id, RemoveMode.Forget, Cancel);

        _watch.Received(1).Forget(Cat.Id);
    }

    [Fact]
    public async Task Does_not_take_another_project_in_the_same_file_for_this_one()
    {
        await ListedAsync(Cat, CatPath);
        _files[CatPath] = Cat with { Id = Guid.NewGuid() };

        var get = () => _service.GetAsync(Cat.Id, Cancel);

        (await get.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectNotFound);
    }

    [Fact]
    public async Task Renames_inside_the_file_and_keeps_the_revision()
    {
        await ListedAsync(Cat, CatPath);

        var renamed = await _service.RenameAsync(Cat.Id, "  Кот на диване ", Cancel);

        _files[CatPath].Should().Be(Cat with { Name = "Кот на диване", SavedAt = Now });
        renamed.Card.Name.Should().Be("Кот на диване");
        await _events.Received(1).ProjectChangedAsync(renamed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_an_empty_name()
    {
        await ListedAsync(Cat, CatPath);

        var rename = () => _service.RenameAsync(Cat.Id, "   ", Cancel);

        var error = (await rename.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("name");
        _files[CatPath].Should().Be(Cat);
    }

    [Fact]
    public async Task Saves_the_scene_over_the_revision_it_was_made_from()
    {
        await ListedAsync(Cat, CatPath);
        var scene = Scenes.Of(Scenes.Segment with { OutFrame = 9 });

        var saved = await _service.SaveSceneAsync(Cat.Id, Cat.Revision, scene, Cancel);

        saved.Should().Be(new SavedScene(13, Now));
        _files[CatPath].Should().Be(Cat with { Scene = scene, Revision = 13, SavedAt = Now });
        _registry.Cards.Single().DurationSec.Should().BeApproximately(1, 1e-9);
        await _events.Received(1).ProjectChangedAsync(Arg.Any<ProjectListItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Records_an_export_in_the_file_without_a_new_revision_or_an_announcement()
    {
        await ListedAsync(Cat, CatPath);
        var export = SampleExports.Of(Cat.Scene.Segments[0], Cat.Source);

        await _service.RecordExportAsync(Cat.Id, export, Cancel);

        var file = _files[CatPath];
        file.Revision.Should().Be(Cat.Revision);
        file.SavedAt.Should().Be(Cat.SavedAt);
        file.Scene.Should().Be(Cat.Scene);
        file.Exports.Should().ContainKey("seg-1").WhoseValue.Should().BeSameAs(export);
        await _events.DidNotReceive().ProjectChangedAsync(Arg.Any<ProjectListItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Keeps_the_exports_when_the_scene_is_saved()
    {
        var export = SampleExports.Of(Cat.Scene.Segments[0], Cat.Source);
        await ListedAsync(Cat.WithExport(export), CatPath);

        await _service.SaveSceneAsync(Cat.Id, Cat.Revision, Scenes.Of(Scenes.Segment with { OutFrame = 9 }), Cancel);

        _files[CatPath].Exports["seg-1"].Should().BeSameAs(export);
    }

    [Fact]
    public async Task Refuses_a_save_made_over_an_old_revision()
    {
        await ListedAsync(Cat, CatPath);

        var save = () => _service.SaveSceneAsync(Cat.Id, Cat.Revision - 1, Scenes.Valid, Cancel);

        (await save.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.RevisionConflict);
        _files[CatPath].Should().Be(Cat);
    }

    [Fact]
    public async Task Lets_only_one_of_two_saves_over_the_same_revision_through()
    {
        await ListedAsync(Cat, CatPath);

        var results = await Task.WhenAll(
            Attempt(() => _service.SaveSceneAsync(Cat.Id, Cat.Revision, Scenes.Valid, Cancel)),
            Attempt(() => _service.SaveSceneAsync(Cat.Id, Cat.Revision, Scenes.Valid, Cancel)));

        results.Should().BeEquivalentTo([(ErrorCode?)null, ErrorCode.RevisionConflict]);
        _files[CatPath].Revision.Should().Be(Cat.Revision + 1);
    }

    [Fact]
    public async Task Refuses_a_scene_that_breaks_its_rules_with_the_broken_fields()
    {
        await ListedAsync(Cat, CatPath);
        var scene = Scenes.Of(Scenes.Segment with { OutFrame = 31 });

        var save = () => _service.SaveSceneAsync(Cat.Id, Cat.Revision, scene, Cancel);

        var error = (await save.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("scene.segments[0].outFrame");
        _files[CatPath].Should().Be(Cat);
    }

    [Fact]
    public async Task Forgets_a_project_and_leaves_its_file()
    {
        await ListedAsync(Cat, CatPath);

        await _service.RemoveAsync(Cat.Id, RemoveMode.Forget, Cancel);

        _registry.Cards.Should().BeEmpty();
        _files.Should().ContainKey(CatPath);
        await _trash.DidNotReceiveWithAnyArgs().MoveToTrashAsync(default!, Cancel);
        await _events.Received(1).ProjectRemovedAsync(Cat.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sends_the_file_to_the_trash_and_takes_it_off_the_list()
    {
        await ListedAsync(Cat, CatPath);

        await _service.RemoveAsync(Cat.Id, RemoveMode.Trash, Cancel);

        await _trash.Received(1).MoveToTrashAsync(CatPath, Arg.Any<CancellationToken>());
        _registry.Cards.Should().BeEmpty();
        await _events.Received(1).ProjectRemovedAsync(Cat.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Keeps_the_project_on_the_list_when_the_trash_refuses_it()
    {
        await ListedAsync(Cat, CatPath);
        _trash.MoveToTrashAsync(CatPath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AppException(ErrorCode.SaveFailed, "Access denied")));

        var remove = () => _service.RemoveAsync(Cat.Id, RemoveMode.Trash, Cancel);

        (await remove.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SaveFailed);
        _registry.Cards.Should().ContainSingle();
        await _events.DidNotReceiveWithAnyArgs().ProjectRemovedAsync(default, Cancel);
    }

    [Fact]
    public async Task Does_not_remove_a_project_off_the_list()
    {
        var remove = () => _service.RemoveAsync(Guid.NewGuid(), RemoveMode.Forget, Cancel);

        (await remove.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectNotFound);
    }

    private static async Task<ErrorCode?> Attempt(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (AppException exception)
        {
            return exception.Code;
        }
    }
}
