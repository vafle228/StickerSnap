using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Projects;

namespace StickerSnap.Tests.Infrastructure.Projects;

/// <summary>.ssproj files in a temporary folder.</summary>
public sealed class ProjectFileStoreTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("stickersnap-projects-");
    private readonly ProjectFileStore _store = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string PathOf(string name) => Path.Combine(_folder.FullName, name);

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public async Task Reads_back_the_project_it_saved()
    {
        var path = PathOf("Кот.ssproj");

        await _store.WriteAsync(path, SampleProjects.Sample, Cancel);
        var project = await _store.ReadAsync(path, Cancel);

        project.Should().BeEquivalentTo(SampleProjects.Sample, SampleProjects.ByValue);
    }

    [Fact]
    public async Task Replaces_the_file_and_leaves_nothing_beside_it()
    {
        var path = PathOf("cat.ssproj");

        await _store.WriteAsync(path, SampleProjects.Sample, Cancel);
        await _store.WriteAsync(path, SampleProjects.Sample with { Revision = 13 }, Cancel);

        _folder.GetFiles().Select(file => file.Name).Should().Equal("cat.ssproj");
        (await _store.ReadAsync(path, Cancel)).Revision.Should().Be(13);
    }

    [Fact]
    public async Task Creates_the_folder_of_a_new_project()
    {
        var path = Path.Combine(_folder.FullName, "Проекты", "cat.ssproj");

        await _store.WriteAsync(path, SampleProjects.Sample, Cancel);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Creates_a_new_project_under_its_name_in_a_folder_it_makes()
    {
        var folder = PathOf("Проекты");
        var project = SampleProjects.Sample with { Name = "cat" };

        var path = await _store.CreateAsync(folder, project, Cancel);

        path.Should().Be(Path.Combine(folder, "cat.ssproj"));
        (await _store.ReadAsync(path, Cancel)).Should().BeEquivalentTo(project, SampleProjects.ByValue);
    }

    [Fact]
    public async Task Numbers_a_new_project_whose_name_is_taken_and_never_overwrites()
    {
        var project = SampleProjects.Sample with { Name = "cat" };
        await File.WriteAllTextAsync(PathOf("cat.ssproj"), "someone else's", Cancel);

        var second = await _store.CreateAsync(_folder.FullName, project, Cancel);
        var third = await _store.CreateAsync(_folder.FullName, project, Cancel);

        Path.GetFileName(second).Should().Be("cat (2).ssproj");
        Path.GetFileName(third).Should().Be("cat (3).ssproj");
        (await File.ReadAllTextAsync(PathOf("cat.ssproj"), Cancel)).Should().Be("someone else's");
        _folder.GetFiles().Select(file => file.Name).Should().BeEquivalentTo("cat.ssproj", "cat (2).ssproj", "cat (3).ssproj");
    }

    [Fact]
    public async Task Reads_the_sample_from_a_disk()
    {
        var path = await TestData.TestFiles.CopyToAsync(SampleProjects.SamplePath, _folder.FullName, Cancel);

        var project = await _store.ReadAsync(path, Cancel);

        project.Id.Should().Be(SampleProjects.Sample.Id);
    }

    [Theory]
    [InlineData("gone.ssproj")]
    [InlineData("gone/cat.ssproj")]
    public async Task Says_a_project_that_is_not_there_is_not_found(string name)
    {
        var read = () => _store.ReadAsync(PathOf(name), Cancel);

        (await read.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectNotFound);
    }

    [Fact]
    public async Task Says_which_file_is_not_a_project()
    {
        var path = PathOf("notes.ssproj");
        await File.WriteAllTextAsync(path, "Купить корм", Cancel);

        var read = () => _store.ReadAsync(path, Cancel);

        var error = (await read.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.ProjectInvalid);
        error.Message.Should().StartWith($"{path} is not a project");
    }

    [Fact]
    public async Task Does_not_load_a_video_given_the_name_of_a_project()
    {
        var path = PathOf("video.ssproj");
        await using (var file = File.Create(path))
            file.SetLength(ProjectFileStore.MaxBytes + 1);

        var read = () => _store.ReadAsync(path, Cancel);

        (await read.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.ProjectInvalid);
    }
}
