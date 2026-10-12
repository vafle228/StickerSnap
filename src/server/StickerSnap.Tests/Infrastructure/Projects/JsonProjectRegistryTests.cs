using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Infrastructure.Projects;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Projects;

/// <summary>projects.json in a temporary data directory.</summary>
public sealed class JsonProjectRegistryTests : IDisposable
{
    private static readonly ProjectCard Cat = ProjectCard.Of(SampleProjects.Sample, "/projects/cat.ssproj");
    private static readonly ProjectCard Dog = Cat with { Id = Guid.NewGuid(), Name = "Пёс", Path = "/projects/dog.ssproj" };

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-registry-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_data.FullName, JsonProjectRegistry.FileName);

    public void Dispose() => _data.Delete(recursive: true);

    private JsonProjectRegistry Registry(string? dataDir = null) => new(
        Options.Create(new DataOptions { DataDir = dataDir ?? _data.FullName }),
        NullLogger<JsonProjectRegistry>.Instance);

    [Fact]
    public async Task Starts_empty_and_writes_nothing_until_a_change()
    {
        (await Registry().ListAsync(Cancel)).Should().BeEmpty();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Reads_back_what_it_saved_after_a_restart()
    {
        var before = Registry();
        await before.SaveAsync(Cat, Cancel);
        await before.SaveAsync(Dog, Cancel);

        var after = await Registry().ListAsync(Cancel);

        after.Should().BeEquivalentTo([Cat, Dog]);
        (await Registry().FindAsync(Dog.Id, Cancel)).Should().Be(Dog);
    }

    [Fact]
    public async Task Replaces_the_card_of_the_same_id()
    {
        var registry = Registry();
        await registry.SaveAsync(Cat, Cancel);

        await registry.SaveAsync(Cat with { Name = "Кот на диване" }, Cancel);

        (await Registry().ListAsync(Cancel)).Should().ContainSingle().Which.Name.Should().Be("Кот на диване");
    }

    [Fact]
    public async Task Forgets_a_removed_project_and_ignores_an_unknown_one()
    {
        var registry = Registry();
        await registry.SaveAsync(Cat, Cancel);
        await registry.SaveAsync(Dog, Cancel);

        await registry.RemoveAsync(Cat.Id, Cancel);
        await registry.RemoveAsync(Guid.NewGuid(), Cancel);

        (await Registry().ListAsync(Cancel)).Should().Equal(Dog);
    }

    [Fact]
    public async Task Keeps_Cyrillic_names_and_paths_readable_in_the_file()
    {
        await Registry().SaveAsync(Cat with { Path = "/home/u/Проекты/кот.ssproj" }, Cancel);

        var text = await File.ReadAllTextAsync(FilePath, Cancel);

        text.Should().Contain("Кот и клавиатура").And.Contain("/home/u/Проекты/кот.ssproj").And.Contain("\"preset\": \"mixed\"");
    }

    [Fact]
    public async Task Sets_a_broken_list_aside_and_starts_empty()
    {
        await File.WriteAllTextAsync(FilePath, """{"version":1,"projects":[{"id":"not a guid"}]}""", Cancel);

        var list = await Registry().ListAsync(Cancel);

        list.Should().BeEmpty();
        File.Exists(FilePath + ".bad").Should().BeTrue();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_the_list_in_memory_as_it_was_when_a_write_fails()
    {
        // A file where the data directory should be: nothing can be written under it
        var blocked = Path.Combine(_data.FullName, "blocked");
        await File.WriteAllTextAsync(blocked, "", Cancel);
        var registry = Registry(Path.Combine(blocked, "data"));

        var save = () => registry.SaveAsync(Cat, Cancel);

        (await save.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SaveFailed);
        (await registry.ListAsync(Cancel)).Should().BeEmpty();
    }
}
