using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.App;
using StickerSnap.Application.Events;
using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Settings;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;
using Samples = StickerSnap.Tests.Core.Render.Media;

namespace StickerSnap.Tests.Application.Projects;

/// <summary>
/// «Новый проект из видео» and «Указать новое видео» over substituted ports: what the new project
/// holds, where it is saved, and that a refused or cancelled reading leaves nothing behind.
/// </summary>
public sealed class ProjectSourceTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\Users\u" : "/home/u";
    private static readonly string ProjectsFolder = Path.Combine(Root, "Проекты");
    private static readonly string VideoPath = Path.Combine(Root, "Видео", "cat.mp4");
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Fingerprint = "v1:3000000:ab";

    private readonly InMemoryProjectRegistry _registry = new();
    private readonly Dictionary<string, Project> _files = new(StringComparer.Ordinal);
    private readonly IProjectStore _store = Substitute.For<IProjectStore>();
    private readonly ISourcePresence _presence = Substitute.For<ISourcePresence>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly ISourceWatch _watch = Substitute.For<ISourceWatch>();
    private readonly IHostInfo _host = Substitute.For<IHostInfo>();
    private readonly IUploadStore _uploads = Substitute.For<IUploadStore>();
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly IFileFingerprint _fingerprints = Substitute.For<IFileFingerprint>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ProjectService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ProjectSourceTests()
    {
        _store.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            _files.TryGetValue(call.Arg<string>(), out var project)
                ? project
                : throw new AppException(ErrorCode.ProjectNotFound, $"No file at {call.Arg<string>()}"));
        _store.WriteAsync(Arg.Any<string>(), Arg.Any<Project>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _files[call.Arg<string>()] = call.Arg<Project>();
            return Task.CompletedTask;
        });
        _store.CreateAsync(ProjectsFolder, Arg.Any<Project>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var path = Path.Combine(ProjectsFolder, ProjectFileNames.Candidates(call.Arg<Project>().Name).First());
            _files[path] = call.Arg<Project>();
            return path;
        });
        _presence.Of(Arg.Any<string>()).Returns(PresenceStatus.Ok);
        _host.Mode.Returns(AppMode.Desktop);
        _uploads.Find(Arg.Any<string>()).Returns((string?)null);
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>()).Returns(Samples.Landscape);
        _fingerprints.OfAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Fingerprint);
        _settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(UserSettings.Default(new Folders(ProjectsFolder, Path.Combine(Root, "Стикеры"))));

        var import = new SourceImport(new SourceFiles(_host, _uploads), _probe, _fingerprints, _uploads);
        _service = new ProjectService(
            _registry, _store, Substitute.For<ITrash>(), _presence, _events, import, _settings, _watch,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Makes_a_project_named_after_the_video_with_the_default_scene_in_the_projects_folder()
    {
        var created = await _service.CreateAsync(new SourceRef.OnDisk(VideoPath), Cancel);

        var project = created.Project;
        project.Name.Should().Be("cat");
        project.Revision.Should().Be(1);
        project.SavedAt.Should().Be(Now);
        project.Source.Should().Be(new ProjectSource(VideoPath, "cat.mp4", Fingerprint, Samples.Landscape));
        project.Scene.Should().BeEquivalentTo(DefaultScene.For(Samples.Landscape), o => o.PreferringRuntimeMemberTypes());
        created.Path.Should().Be(Path.Combine(ProjectsFolder, "cat.ssproj"));
        created.SourceStatus.Should().Be(PresenceStatus.Ok);
    }

    [Fact]
    public async Task Puts_the_new_project_on_the_list_and_tells_the_windows()
    {
        var created = await _service.CreateAsync(new SourceRef.OnDisk(VideoPath), Cancel);

        (await _registry.FindAsync(created.Project.Id, Cancel)).Should().Be(ProjectCard.Of(created.Project, created.Path));
        await _events.Received(1).ProjectChangedAsync(
            Arg.Is<ProjectListItem>(item => item.Card.Id == created.Project.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Takes_an_upload_for_good_and_keeps_the_project_at_the_taken_file()
    {
        var uploaded = Path.Combine(Root, "data", "uploads", "u1", "cat.gif");
        var kept = Path.Combine(Root, "data", "sources", "u1", "cat.gif");
        _uploads.Find("u1").Returns(uploaded);
        _uploads.KeepAsync("u1", Arg.Any<CancellationToken>()).Returns(kept);
        _probe.ProbeAsync(uploaded, Arg.Any<CancellationToken>()).Returns(Scenes.Source);

        var created = await _service.CreateAsync(new SourceRef.Upload("u1"), Cancel);

        created.Project.Source.Path.Should().Be(kept);
        created.Project.Source.FileName.Should().Be("cat.gif");
        await _fingerprints.Received(1).OfAsync(uploaded, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_path_in_web_mode_and_saves_nothing()
    {
        _host.Mode.Returns(AppMode.Web);

        var code = await CodeOf(() => _service.CreateAsync(new SourceRef.OnDisk(VideoPath), Cancel));

        code.Should().Be(ErrorCode.BadRequest);
        await NothingWasMadeAsync();
    }

    [Fact]
    public async Task Leaves_nothing_when_the_reading_is_cancelled()
    {
        _uploads.Find("u1").Returns(VideoPath);
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>()).Returns<MediaInfo>(_ => throw new OperationCanceledException());

        var create = () => _service.CreateAsync(new SourceRef.Upload("u1"), Cancel);

        await create.Should().ThrowAsync<OperationCanceledException>();
        await _uploads.DidNotReceiveWithAnyArgs().KeepAsync(default!, Cancel);
        await NothingWasMadeAsync();
    }

    [Fact]
    public async Task Passes_on_a_video_the_engine_cannot_read_and_saves_nothing()
    {
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>())
            .Returns<MediaInfo>(_ => throw new AppException(ErrorCode.SourceUnreadable, "moov atom not found") { Log = "moov atom not found" });

        var code = await CodeOf(() => _service.CreateAsync(new SourceRef.OnDisk(VideoPath), Cancel));

        code.Should().Be(ErrorCode.SourceUnreadable);
        await NothingWasMadeAsync();
    }

    [Fact]
    public async Task Refuses_a_video_without_frames()
    {
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>()).Returns(Samples.Landscape with { FrameCount = 0 });

        var code = await CodeOf(() => _service.CreateAsync(new SourceRef.OnDisk(VideoPath), Cancel));

        code.Should().Be(ErrorCode.SourceUnreadable);
        await NothingWasMadeAsync();
    }

    [Fact]
    public async Task Replaces_the_source_and_names_the_segments_past_its_end()
    {
        var path = Path.Combine(ProjectsFolder, "cat.ssproj");
        var cat = SampleProjects.Sample;
        _files[path] = cat;
        await _registry.SaveAsync(ProjectCard.Of(cat, path), Cancel);
        // Sample's segments run to frames 29 and 20; the new video has 25
        var shorter = Scenes.Source with { FrameCount = 25, DurationSec = 2.5 };
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>()).Returns(shorter);

        var replaced = await _service.ReplaceSourceAsync(cat.Id, new SourceRef.OnDisk(VideoPath), Cancel);

        replaced.SegmentsOutOfRange.Should().Equal("seg-1");
        var saved = _files[path];
        saved.Source.Should().Be(new ProjectSource(VideoPath, "cat.mp4", Fingerprint, shorter));
        saved.Revision.Should().Be(cat.Revision, "only a save of the scene moves the revision (G7)");
        saved.Scene.Should().BeSameAs(cat.Scene);
        saved.SavedAt.Should().Be(Now);
        replaced.Opened.Project.Should().Be(saved);
        await _events.Received(1).ProjectChangedAsync(Arg.Any<ProjectListItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Warns_about_nothing_when_every_segment_fits_the_new_video()
    {
        var path = Path.Combine(ProjectsFolder, "cat.ssproj");
        _files[path] = SampleProjects.Sample;
        await _registry.SaveAsync(ProjectCard.Of(SampleProjects.Sample, path), Cancel);
        _probe.ProbeAsync(VideoPath, Arg.Any<CancellationToken>()).Returns(Scenes.Source);

        var replaced = await _service.ReplaceSourceAsync(SampleProjects.Sample.Id, new SourceRef.OnDisk(VideoPath), Cancel);

        replaced.SegmentsOutOfRange.Should().BeEmpty();
    }

    [Fact]
    public async Task Replaces_no_source_of_a_project_it_does_not_know()
    {
        var code = await CodeOf(() => _service.ReplaceSourceAsync(Guid.NewGuid(), new SourceRef.OnDisk(VideoPath), Cancel));

        code.Should().Be(ErrorCode.ProjectNotFound);
        await _probe.DidNotReceiveWithAnyArgs().ProbeAsync(default!, Cancel);
    }

    private async Task NothingWasMadeAsync()
    {
        await _store.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, Cancel);
        (await _registry.ListAsync(Cancel)).Should().BeEmpty();
        await _events.DidNotReceiveWithAnyArgs().ProjectChangedAsync(default!, Cancel);
    }

    private static async Task<ErrorCode> CodeOf(Func<Task> action)
    {
        var exception = await action.Should().ThrowAsync<AppException>();
        return exception.Which.Code;
    }
}
