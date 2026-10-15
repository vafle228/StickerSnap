using StickerSnap.Application.Events;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Settings;

namespace StickerSnap.Tests.Application.Settings;

/// <summary>A change is checked, its new folders prepared, then saved and announced, in that order.</summary>
public sealed class SettingsServiceTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\Users\u" : "/home/u";
    private static readonly string Projects = Path.Combine(Root, "Projects");
    private static readonly string Stickers = Path.Combine(Root, "Stickers");
    private static readonly UserSettings Saved = UserSettings.Default(new Folders(Projects, Stickers));

    private readonly ISettingsRepository _repository = Substitute.For<ISettingsRepository>();
    private readonly IFolderAccess _folders = Substitute.For<IFolderAccess>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly SettingsService _service;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public SettingsServiceTests()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Saved);
        _service = new SettingsService(_repository, _folders, _events);
    }

    [Fact]
    public async Task Gives_the_saved_settings()
    {
        (await _service.GetAsync(Cancel)).Should().Be(Saved);
    }

    [Fact]
    public async Task Saves_the_change_and_announces_the_result()
    {
        var updated = await _service.UpdateAsync(new SettingsChange(Theme: Theme.Dark), Cancel);

        updated.Should().Be(Saved with { Theme = Theme.Dark });
        Received.InOrder(() =>
        {
            _repository.SaveAsync(updated, Arg.Any<CancellationToken>());
            _events.SettingsChangedAsync(updated, Arg.Any<CancellationToken>());
        });
        await _folders.DidNotReceiveWithAnyArgs().PrepareAsync(default!, Cancel);
    }

    [Fact]
    public async Task Prepares_only_the_folder_that_changed_before_saving()
    {
        var output = Path.Combine(Root, "Out");

        var updated = await _service.UpdateAsync(new SettingsChange(StickersFolder: output, ProjectsFolder: Projects), Cancel);

        updated.Folders.Should().Be(new Folders(Projects, output));
        Received.InOrder(() =>
        {
            _folders.PrepareAsync(output, Arg.Any<CancellationToken>());
            _repository.SaveAsync(updated, Arg.Any<CancellationToken>());
        });
        await _folders.DidNotReceive().PrepareAsync(Projects, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("  ", "empty")]
    [InlineData("Stickers", "not_absolute")]
    [InlineData("../Stickers", "not_absolute")]
    public async Task Refuses_a_folder_that_is_not_an_absolute_path(string folder, string problem)
    {
        var update = () => _service.UpdateAsync(new SettingsChange(StickersFolder: folder), Cancel);

        var error = (await update.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("folders.stickers").WhoseValue.Should().Equal(problem);
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }

    [Fact]
    public async Task Refuses_a_change_that_names_no_field()
    {
        var update = () => _service.UpdateAsync(new SettingsChange(), Cancel);

        (await update.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BadRequest);
    }

    [Fact]
    public async Task Saves_nothing_and_announces_nothing_when_a_folder_cannot_be_written()
    {
        var output = Path.Combine(Root, "Locked");
        _folders.PrepareAsync(output, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AppException(ErrorCode.SaveFailed, "Access denied")));

        var update = () => _service.UpdateAsync(new SettingsChange(Theme: Theme.Dark, StickersFolder: output), Cancel);

        (await update.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SaveFailed);
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
        await _events.DidNotReceiveWithAnyArgs().SettingsChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Announces_nothing_when_saving_fails()
    {
        _repository.SaveAsync(Arg.Any<UserSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AppException(ErrorCode.DiskFull, "No space")));

        var update = () => _service.UpdateAsync(new SettingsChange(Accent: Accent.Teal), Cancel);

        (await update.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.DiskFull);
        await _events.DidNotReceiveWithAnyArgs().SettingsChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Writes_nothing_for_a_change_to_what_is_already_there()
    {
        var updated = await _service.UpdateAsync(new SettingsChange(Theme: Theme.System, ProjectsFolder: Projects), Cancel);

        updated.Should().Be(Saved);
        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
        await _events.DidNotReceiveWithAnyArgs().SettingsChangedAsync(default!, Cancel);
    }

    [Fact]
    public async Task Remembers_the_emoji_of_a_publication_and_announces_them()
    {
        var updated = await _service.RememberEmojiAsync(["🐱", "😂"], Cancel);

        updated.RecentEmoji.Should().Equal("🐱", "😂");
        await _repository.Received(1).SaveAsync(updated, Arg.Any<CancellationToken>());
        await _events.Received(1).SettingsChangedAsync(updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saves_nothing_when_the_recent_emoji_stay_as_they_were()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(Saved with { RecentEmoji = ["🐱", "😂"] });

        await _service.RememberEmojiAsync(["🐱"], Cancel);

        await _repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, Cancel);
    }
}
