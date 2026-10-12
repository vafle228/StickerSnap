using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Settings;
using StickerSnap.Infrastructure.Settings;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Settings;

/// <summary>settings.json in a temporary data directory.</summary>
public sealed class JsonSettingsRepositoryTests : IDisposable
{
    private static readonly Folders Defaults = new("/defaults/Проекты", "/defaults/Стикеры");

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-settings-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_data.FullName, JsonSettingsRepository.FileName);

    public void Dispose() => _data.Delete(recursive: true);

    private JsonSettingsRepository Repository(string? dataDir = null) => new(
        Options.Create(new DataOptions { DataDir = dataDir ?? _data.FullName }),
        () => Defaults,
        NullLogger<JsonSettingsRepository>.Instance);

    [Fact]
    public async Task Gives_the_defaults_on_a_first_launch_and_writes_nothing()
    {
        var settings = await Repository().GetAsync(Cancel);

        settings.Should().BeEquivalentTo(UserSettings.Default(Defaults));
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Reads_back_what_it_saved_after_a_restart()
    {
        var saved = new UserSettings(Theme.Dark, Accent.Raspberry, new Folders("/p", "/s"), ["😂", "🔥"]);

        await Repository().SaveAsync(saved, Cancel);
        var read = await Repository().GetAsync(Cancel);

        read.Should().BeEquivalentTo(saved);
    }

    [Fact]
    public async Task Writes_the_file_in_the_words_of_the_contract()
    {
        await Repository().SaveAsync(new UserSettings(Theme.Dark, Accent.Graphite, new Folders("/p", "/s"), []), Cancel);

        var text = await File.ReadAllTextAsync(FilePath, Cancel);
        text.Should().Contain("\"version\": 1").And.Contain("\"theme\": \"dark\"").And.Contain("\"accent\": \"graphite\"")
            .And.Contain("\"projects\": \"/p\"").And.Contain("\"recentEmoji\": []");
    }

    [Fact]
    public async Task Leaves_no_temporary_file_beside_the_settings()
    {
        var repository = Repository();

        await repository.SaveAsync(UserSettings.Default(Defaults), Cancel);
        await repository.SaveAsync(UserSettings.Default(Defaults) with { Theme = Theme.Light }, Cancel);

        _data.GetFiles().Select(file => file.Name).Should().Equal(JsonSettingsRepository.FileName);
    }

    [Fact]
    public async Task Creates_a_data_directory_that_does_not_exist_yet()
    {
        var dataDir = Path.Combine(_data.FullName, "nested", "StickerSnap");

        await Repository(dataDir).SaveAsync(UserSettings.Default(Defaults), Cancel);

        File.Exists(Path.Combine(dataDir, JsonSettingsRepository.FileName)).Should().BeTrue();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"version":1,"theme":"dark","folders":{"projects":"/p","stickers":"/s"},"recentEmoji":[]}""")]
    [InlineData("""{"version":1,"theme":"neon","accent":"indigo","folders":{"projects":"/p","stickers":"/s"}}""")]
    [InlineData("""{"version":1,"theme":"dark","accent":"indigo","folders":{"projects":"/p"}}""")]
    [InlineData("null")]
    public async Task Sets_a_broken_file_aside_and_falls_back_to_the_defaults(string broken)
    {
        await File.WriteAllTextAsync(FilePath, broken, Cancel);

        var settings = await Repository().GetAsync(Cancel);

        settings.Should().BeEquivalentTo(UserSettings.Default(Defaults));
        File.Exists(FilePath).Should().BeFalse();
        (await File.ReadAllTextAsync(FilePath + ".bad", Cancel)).Should().Be(broken);
    }

    [Fact]
    public async Task Reads_a_file_without_recent_emoji_as_having_none()
    {
        await File.WriteAllTextAsync(FilePath,
            """{"version":1,"theme":"light","accent":"teal","folders":{"projects":"/p","stickers":"/s"}}""", Cancel);

        var settings = await Repository().GetAsync(Cancel);

        settings.Should().BeEquivalentTo(new UserSettings(Theme.Light, Accent.Teal, new Folders("/p", "/s"), []));
    }

    [Fact]
    public async Task Fails_to_save_where_a_file_stands_in_place_of_the_data_directory()
    {
        var dataDir = Path.Combine(_data.FullName, "taken");
        await File.WriteAllTextAsync(dataDir, "a file where the directory should be", Cancel);

        var save = () => Repository(dataDir).SaveAsync(UserSettings.Default(Defaults), Cancel);

        (await save.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SaveFailed);
    }

    [Fact]
    public async Task Gives_the_saved_settings_without_reading_the_file_again()
    {
        var repository = Repository();
        var saved = UserSettings.Default(Defaults) with { Accent = Accent.Violet };
        await repository.SaveAsync(saved, Cancel);

        File.Delete(FilePath);

        (await repository.GetAsync(Cancel)).Should().BeEquivalentTo(saved);
    }
}
