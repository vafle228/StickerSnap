using StickerSnap.Core.Settings;

namespace StickerSnap.Tests.Core.Settings;

/// <summary>A change replaces only the fields it names, folders one by one.</summary>
public sealed class UserSettingsTests
{
    private static readonly UserSettings Current = new(
        Theme.Dark, Accent.Teal, new Folders("/home/u/Projects", "/home/u/Stickers"), ["😂", "🔥"]);

    [Fact]
    public void Starts_with_the_system_theme_indigo_and_no_emoji()
    {
        var folders = new Folders("/p", "/s");

        UserSettings.Default(folders).Should().BeEquivalentTo(new UserSettings(Theme.System, Accent.Indigo, folders, []));
    }

    [Fact]
    public void Changes_the_theme_alone()
    {
        var changed = Current.With(new SettingsChange(Theme: Theme.Light));

        changed.Should().BeEquivalentTo(Current with { Theme = Theme.Light });
    }

    [Fact]
    public void Changes_one_folder_and_keeps_the_other()
    {
        var changed = Current.With(new SettingsChange(StickersFolder: "/mnt/out"));

        changed.Folders.Should().Be(new Folders("/home/u/Projects", "/mnt/out"));
        changed.Theme.Should().Be(Theme.Dark);
        changed.Accent.Should().Be(Accent.Teal);
    }

    [Fact]
    public void Keeps_the_recent_emoji_which_only_the_server_writes()
    {
        var changed = Current.With(new SettingsChange(Accent: Accent.Violet, ProjectsFolder: "/p"));

        changed.RecentEmoji.Should().Equal("😂", "🔥");
    }

    [Fact]
    public void Knows_an_empty_change()
    {
        new SettingsChange().IsEmpty.Should().BeTrue();
        new SettingsChange(ProjectsFolder: "/p").IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Puts_the_emoji_just_sent_first_each_once()
    {
        Current.WithRecentEmoji(["🐱", "🔥", "🐱"]).RecentEmoji.Should().Equal("🐱", "🔥", "😂");
    }

    [Fact]
    public void Keeps_at_most_two_dozen_recent_emoji()
    {
        var many = Enumerable.Range(0, 30).Select(i => char.ConvertFromUtf32(0x1F600 + i)).ToList();

        Current.WithRecentEmoji(many).RecentEmoji.Should().Equal(many.Take(UserSettings.MaxRecentEmoji));
    }
}
