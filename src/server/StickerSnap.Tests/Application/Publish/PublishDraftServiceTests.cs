using StickerSnap.Application.Account;
using StickerSnap.Application.Packs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Publish;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Account;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;
using StickerSnap.Core.Settings;
using StickerSnap.Core.Validation;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Publish;

/// <summary>What the publish window opens with, read from the project, the registry and the settings.</summary>
public sealed class PublishDraftServiceTests
{
    private static readonly Project Cat = SampleProjects.Sample;
    private static readonly StickerOrigin FromSeg1 = new(Cat.Id, Cat.Name, "seg-1");

    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly IPackService _packs = Substitute.For<IPackService>();
    private readonly IAccountService _account = Substitute.For<IAccountService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private PublishDraftService Service => new(_projects, _packs, _account, _settings);

    public PublishDraftServiceTests()
    {
        var exports = new Dictionary<string, SegmentExport>
        {
            ["seg-1"] = SampleExports.Of(Cat.Scene.Segments[0], Cat.Source),
            // Made before the segment was edited
            ["seg-2"] = SampleExports.Of(Cat.Scene.Segments[1] with { OutFrame = 19 }, Cat.Source) with
            {
                Validation = SampleExports.Green with { Verdict = ValidationVerdict.Fail },
            },
        };
        _projects.GetAsync(Cat.Id, Arg.Any<CancellationToken>())
            .Returns(new OpenedProject(Cat with { Exports = exports }, "/p/cat.ssproj", PresenceStatus.Ok));
        _account.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountLink.Linked(new LinkedUser(42, "Дима", null)));
        _settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(UserSettings.Default(new Folders("/p", "/s")).WithRecentEmoji(["😂", "🔥"]));
        _packs.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Pack("first_by_StickerSnapAppBot", PackKind.Regular, 3, FromSeg1),
            Pack("full_by_StickerSnapAppBot", PackKind.Regular, 120, null),
            Pack("emoji_by_StickerSnapAppBot", PackKind.CustomEmoji, 1, null),
            Pack("last_by_StickerSnapAppBot", PackKind.Regular, 2, FromSeg1),
        ]);
    }

    private static Pack Pack(string name, PackKind kind, int count, StickerOrigin? origin) =>
        new(name, name, kind, PresenceStatus.Ok, null,
            [.. Enumerable.Range(0, count).Select(i => new PackSticker($"{name[..2]}{i}", $"F{i}", ["🐱"], i == 0 ? origin : null))]);

    [Fact]
    public async Task Gives_each_segment_its_length_export_and_where_it_was_sent()
    {
        var draft = await Service.GetAsync(Cat.Id, Cancel);

        draft.Segments.Select(segment => segment.SegmentId).Should().Equal("seg-1", "seg-2");
        var first = draft.Segments[0];
        first.Export.Should().Be(new DraftExport(190_464, ValidationVerdict.Pass, Stale: false));
        first.DurationSec.Should().Be(Cat.Scene.Segments[0].PlaySeconds(Cat.Source));
        first.PublishedIn.Should().Equal(
            new StickerRef("first_by_StickerSnapAppBot", "fi0"), new StickerRef("last_by_StickerSnapAppBot", "la0"));
        draft.Segments[1].Export.Should().Be(new DraftExport(190_464, ValidationVerdict.Fail, Stale: true));
        draft.Segments[1].PublishedIn.Should().BeEmpty();
    }

    [Fact]
    public async Task Names_the_last_pack_the_project_went_to()
    {
        (await Service.GetAsync(Cat.Id, Cancel)).LastPack.Should().Be("last_by_StickerSnapAppBot");
    }

    [Fact]
    public async Task Tells_why_a_pack_cannot_be_picked_for_a_mix_of_stickers_and_emoji()
    {
        var draft = await Service.GetAsync(Cat.Id, Cancel);

        draft.Packs.Select(pack => (pack.Pack.Name, pack.Refusal)).Should().Equal(
            ("first_by_StickerSnapAppBot", null),
            ("full_by_StickerSnapAppBot", PackRefusal.Full),
            ("emoji_by_StickerSnapAppBot", PackRefusal.WrongType),
            ("last_by_StickerSnapAppBot", null));
    }

    [Fact]
    public async Task Carries_the_account_and_the_recent_emoji()
    {
        var draft = await Service.GetAsync(Cat.Id, Cancel);

        draft.Account.Should().BeOfType<AccountLink.Linked>();
        draft.RecentEmoji.Should().Equal("😂", "🔥");
    }
}
