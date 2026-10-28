using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Events;
using StickerSnap.Application.Packs;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Application.Projects;

namespace StickerSnap.Tests.Application.Packs;

/// <summary>The registry of packs against a faked Telegram: the list, checks, «Добавить по ссылке», names.</summary>
public sealed class PackServiceTests : IDisposable
{
    private const string Cats = "kot_divan_by_StickerSnapAppBot";

    private static readonly Guid CatProject = Guid.Parse("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51");
    private static readonly Guid GoneProject = Guid.Parse("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e59");
    private static readonly DateTimeOffset Before = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPackRepository _packs = new();
    private readonly IStickerPublisher _telegram = Substitute.For<IStickerPublisher>();
    private readonly InMemoryProjectRegistry _projects = new();
    private readonly ISourcePresence _presence = Substitute.For<ISourcePresence>();
    private readonly TempMediaCache _cache = new();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private PackService Service => new(_packs, _telegram, _projects, _presence, _cache, _events, _clock);

    public void Dispose() => _cache.Dispose();

    private static Pack Known() => new(Cats, "Кот и диван", PackKind.Regular, PresenceStatus.Ok, Before,
    [
        new PackSticker("U1", "F1", ["😂", "🐱"], new StickerOrigin(CatProject, "Кот", "s1")),
        new PackSticker("U2", "F2", ["😴"], new StickerOrigin(GoneProject, "Старый", "s1")),
        new PackSticker("U3", "F3", ["👍"], null),
    ]);

    private static PublishedPack InTelegram(params PublishedSticker[] stickers) => new(Cats, "Кот и диван", PackKind.Regular, stickers);

    private void Telegram(string name, PublishedPack? pack) => _telegram.GetPackAsync(name, Arg.Any<CancellationToken>()).Returns(pack);

    /// <summary>Whether the service says a pack of that name exists, whoever made it.</summary>
    private void Taken(string name, bool taken) => _telegram.IsNameTakenAsync(name, Arg.Any<CancellationToken>()).Returns(taken);

    private async Task WithKnownAsync()
    {
        await _packs.SaveAsync(Known(), Cancel);
        await _projects.SaveAsync(new ProjectCard(CatProject, "Кот на диване", "/p/cat.ssproj", "/v/cat.mp4", Before, 2, CardPreset.Sticker, 2.5), Cancel);
        _presence.Of("/p/cat.ssproj").Returns(PresenceStatus.Ok);
    }

    [Fact]
    public async Task Lists_the_registry_without_asking_telegram()
    {
        await WithKnownAsync();

        (await Service.ListAsync(Cancel)).Should().BeEquivalentTo([Known()]);
        _telegram.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Gives_each_sticker_its_project_as_the_project_list_has_it_now()
    {
        await WithKnownAsync();

        var view = await Service.GetAsync("KOT_DIVAN_by_StickerSnapAppBot", Cancel);

        view.Stickers.Select(sticker => sticker.Origin).Should().Equal(
            new OriginView(CatProject, "Кот на диване", "s1", PresenceStatus.Ok),
            new OriginView(GoneProject, "Старый", "s1", PresenceStatus.Missing),
            null);
    }

    [Fact]
    public async Task Calls_a_project_whose_file_is_gone_missing()
    {
        await WithKnownAsync();
        _presence.Of("/p/cat.ssproj").Returns(PresenceStatus.Missing);

        var view = await Service.GetAsync(Cats, Cancel);

        view.Stickers[0].Origin!.ProjectStatus.Should().Be(PresenceStatus.Missing);
    }

    [Fact]
    public async Task Knows_no_pack_off_the_list()
    {
        var getting = () => Service.GetAsync(Cats, Cancel);

        (await getting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNotFound);
    }

    [Fact]
    public async Task Writes_a_check_with_telegram_into_the_registry_and_says_so()
    {
        await WithKnownAsync();
        Telegram(Cats, InTelegram(new("F1", "U1", "😂"), new("F4", "U4", "🔥")));

        var view = await Service.SyncAsync(Cats, Cancel);

        var expected = Known() with
        {
            CheckedAt = _clock.GetUtcNow(),
            Stickers = [Known().Stickers[0], new PackSticker("U4", "F4", ["🔥"], null)],
        };
        view.Pack.Should().BeEquivalentTo(expected);
        _packs.Packs.Should().BeEquivalentTo([expected]);
        PackEvents.Sent(_events).Should().BeEquivalentTo([expected]);
    }

    [Fact]
    public async Task Marks_a_pack_telegram_no_longer_has_missing()
    {
        await WithKnownAsync();
        Telegram(Cats, null);

        var view = await Service.SyncAsync(Cats, Cancel);

        view.Pack.Status.Should().Be(PresenceStatus.Missing);
        view.Pack.Stickers.Should().HaveCount(3);
        _packs.Packs.Single().Status.Should().Be(PresenceStatus.Missing);
    }

    [Fact]
    public async Task Changes_nothing_when_telegram_asks_to_wait()
    {
        await WithKnownAsync();
        _telegram.GetPackAsync(Cats, Arg.Any<CancellationToken>()).Returns<PublishedPack?>(_ =>
            throw new AppException(ErrorCode.RateLimited, "429") { Params = new Dictionary<string, object> { ["retryAfterSec"] = 24 } });

        var syncing = () => Service.SyncAsync(Cats, Cancel);

        var error = (await syncing.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.RateLimited);
        error.Params["retryAfterSec"].Should().Be(24);
        _packs.Packs.Should().BeEquivalentTo([Known()]);
        PackEvents.Sent(_events).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://t.me/addstickers/cats_by_OtherBot")]
    [InlineData("https://t.me/addstickers/Animals")]
    public async Task Refuses_a_pack_of_another_bot_without_asking_telegram(string link)
    {
        var importing = () => Service.ImportAsync(link, Cancel);

        (await importing.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.NotOurPack);
        _telegram.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_link_that_names_no_pack()
    {
        var importing = () => Service.ImportAsync("https://example.com/котики", Cancel);

        var error = (await importing.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey("link");
    }

    [Fact]
    public async Task Says_telegram_has_no_such_pack()
    {
        Telegram(Cats, null);

        var importing = () => Service.ImportAsync($"https://t.me/addstickers/{Cats}", Cancel);

        (await importing.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNotFound);
        _packs.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task Adds_a_pack_by_its_link()
    {
        Telegram(Cats, InTelegram(new PublishedSticker("F1", "U1", "😂")));

        var view = await Service.ImportAsync($"t.me/addstickers/{Cats}", Cancel);

        var expected = new Pack(Cats, "Кот и диван", PackKind.Regular, PresenceStatus.Ok, _clock.GetUtcNow(), [new PackSticker("U1", "F1", ["😂"], null)]);
        view.Pack.Should().BeEquivalentTo(expected);
        _packs.Packs.Should().BeEquivalentTo([expected]);
        PackEvents.Sent(_events).Should().HaveCount(1);
    }

    [Fact]
    public async Task Copies_any_pack_into_a_new_pack_named_by_its_title()
    {
        const string copy = "starye_koty_by_StickerSnapAppBot";
        Taken(copy, false);
        _telegram.CopyPackAsync("OldCats", copy, "Старые коты", Arg.Any<CancellationToken>()).Returns(
            new PublishedPack(copy, "Старые коты", PackKind.Regular,
            [
                new PublishedSticker("F1", "U1", "😂", StickerFormat.Static),
                new PublishedSticker("F2", "U2", "😴", StickerFormat.Video),
            ]));

        var view = await Service.CopyAsync("https://t.me/addstickers/OldCats", " Старые коты ", Cancel);

        var expected = new Pack(copy, "Старые коты", PackKind.Regular, PresenceStatus.Ok, _clock.GetUtcNow(),
        [
            new PackSticker("U1", "F1", ["😂"], null, StickerFormat.Static),
            new PackSticker("U2", "F2", ["😴"], null, StickerFormat.Video),
        ]);
        view.Pack.Should().BeEquivalentTo(expected);
        _packs.Packs.Should().BeEquivalentTo([expected]);
        PackEvents.Sent(_events).Should().HaveCount(1);
    }

    [Fact]
    public async Task Takes_the_first_free_variant_of_a_taken_name()
    {
        Taken("koty_by_StickerSnapAppBot", true);
        Taken("koty_2_by_StickerSnapAppBot", false);
        _telegram.CopyPackAsync("OldCats", "koty_2_by_StickerSnapAppBot", "Коты", Arg.Any<CancellationToken>())
            .Returns(new PublishedPack("koty_2_by_StickerSnapAppBot", "Коты", PackKind.Regular, [new PublishedSticker("F1", "U1", "😂")]));

        var view = await Service.CopyAsync("OldCats", "Коты", Cancel);

        view.Pack.Name.Should().Be("koty_2_by_StickerSnapAppBot");
    }

    [Fact]
    public async Task A_title_without_letters_names_the_copy_after_its_source()
    {
        Taken("oldcats_by_StickerSnapAppBot", false);
        _telegram.CopyPackAsync("OldCats", "oldcats_by_StickerSnapAppBot", "😺😺", Arg.Any<CancellationToken>())
            .Returns(new PublishedPack("oldcats_by_StickerSnapAppBot", "😺😺", PackKind.Regular, [new PublishedSticker("F1", "U1", "😂")]));

        var view = await Service.CopyAsync("OldCats", "😺😺", Cancel);

        view.Pack.Name.Should().Be("oldcats_by_StickerSnapAppBot");
    }

    [Theory]
    [InlineData("https://t.me/addstickers/", "Коты", "link")]
    [InlineData("OldCats", "  ", "title")]
    public async Task Refuses_a_copy_without_a_source_or_a_title(string link, string title, string field)
    {
        var copying = () => Service.CopyAsync(link, title, Cancel);

        var refused = (await copying.Should().ThrowAsync<AppException>()).Which;
        refused.Code.Should().Be(ErrorCode.BadRequest);
        refused.Fields.Should().ContainKey(field);
        _telegram.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Says_the_name_is_taken_when_no_variant_is_free()
    {
        _telegram.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var copying = () => Service.CopyAsync("OldCats", "Коты", Cancel);

        (await copying.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNameTaken);
        await _telegram.DidNotReceiveWithAnyArgs().CopyPackAsync(default!, default!, default!, Cancel);
    }

    [Fact]
    public async Task Keeps_the_projects_of_a_pack_added_again()
    {
        await WithKnownAsync();
        Telegram(Cats, InTelegram(new PublishedSticker("F1", "U1", "😂")));

        var view = await Service.ImportAsync($"https://t.me/addstickers/{Cats}", Cancel);

        view.Pack.Stickers.Should().BeEquivalentTo([Known().Stickers[0]]);
        _packs.Packs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Forgets_a_pack_and_leaves_telegram_alone()
    {
        await WithKnownAsync();

        await Service.ForgetAsync(Cats, Cancel);

        _packs.Packs.Should().BeEmpty();
        _telegram.ReceivedCalls().Should().BeEmpty();
        var forgetting = () => Service.ForgetAsync(Cats, Cancel);
        (await forgetting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNotFound);
    }

    [Fact]
    public async Task Deletes_a_sticker_by_the_file_id_telegram_gives_now()
    {
        await WithKnownAsync();
        Telegram(Cats, InTelegram(new("F1new", "U1", "😂"), new("F2", "U2", "😴"), new("F3", "U3", "👍")));

        await Service.DeleteStickerAsync(Cats, "U1", Cancel);

        await _telegram.Received(1).DeleteStickerAsync(Cats, "F1new", Arg.Any<CancellationToken>());
        _packs.Packs.Single().Stickers.Select(sticker => sticker.Id).Should().Equal("U2", "U3");
        PackEvents.Sent(_events).Single().Count.Should().Be(2);
    }

    [Fact]
    public async Task Takes_a_sticker_already_gone_from_telegram_as_deleted()
    {
        await WithKnownAsync();
        Telegram(Cats, InTelegram(new PublishedSticker("F2", "U2", "😴")));

        await Service.DeleteStickerAsync(Cats, "U1", Cancel);

        await _telegram.DidNotReceive().DeleteStickerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _packs.Packs.Single().Stickers.Select(sticker => sticker.Id).Should().Equal("U2");
    }

    [Fact]
    public async Task Says_the_pack_is_gone_when_deleting_from_a_pack_telegram_no_longer_has()
    {
        await WithKnownAsync();
        Telegram(Cats, null);

        var deleting = () => Service.DeleteStickerAsync(Cats, "U1", Cancel);

        (await deleting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.PackNotFound);
        _packs.Packs.Single().Status.Should().Be(PresenceStatus.Missing);
        PackEvents.Sent(_events).Single().Status.Should().Be(PresenceStatus.Missing);
    }

    [Fact]
    public async Task Downloads_a_stickers_file_once_and_serves_it_from_disk_after()
    {
        await WithKnownAsync();
        _telegram.DownloadAsync("F1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x1A, 0x45, 0xDF, 0xA3], Cancel));

        var first = await Service.StickerMediaAsync(Cats, "U1", Cancel);
        var second = await Service.StickerMediaAsync(Cats, "U1", Cancel);

        first.ContentType.Should().Be("video/webm");
        second.Should().Be(first);
        File.ReadAllBytes(first.Path).Should().Equal(0x1A, 0x45, 0xDF, 0xA3);
        _cache.Made.Should().Be(1);
        await _telegram.Received(1).DownloadAsync("F1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StickerFormat.Static, ".webp", "image/webp")]
    [InlineData(StickerFormat.Animated, ".tgs", "application/x-tgsticker")]
    public async Task Serves_a_copied_stickers_file_as_what_it_is(StickerFormat format, string extension, string contentType)
    {
        await _packs.SaveAsync(Known() with { Stickers = [new PackSticker("U1", "F1", ["😂"], null, format)] }, Cancel);
        _telegram.DownloadAsync("F1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => File.WriteAllBytesAsync(call.ArgAt<string>(1), [1, 2, 3], Cancel));

        var file = await Service.StickerMediaAsync(Cats, "U1", Cancel);

        file.ContentType.Should().Be(contentType);
        Path.GetExtension(file.Path).Should().Be(extension);
    }

    [Fact]
    public async Task Has_no_file_for_a_sticker_the_pack_does_not_have()
    {
        await WithKnownAsync();

        var getting = () => Service.StickerMediaAsync(Cats, "U9", Cancel);

        (await getting.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.FileNotFound);
    }

    [Fact]
    public async Task Makes_a_free_link_from_the_title()
    {
        Taken("kot_i_divan_by_StickerSnapAppBot", false);

        var check = await Service.CheckNameAsync("Кот и диван", null, Cancel);

        check.Should().BeEquivalentTo(new PackNameCheck("kot_i_divan", "kot_i_divan_by_StickerSnapAppBot", [], null));
    }

    [Fact]
    public async Task Offers_the_first_free_variant_of_a_name_taken_in_telegram()
    {
        Taken(Cats, true);
        Taken("kot_divan_2_by_StickerSnapAppBot", true);
        Taken("kot_divan_3_by_StickerSnapAppBot", false);

        var check = await Service.CheckNameAsync("Кот и диван", "kot_divan", Cancel);

        check.Should().BeEquivalentTo(new PackNameCheck("kot_divan", Cats, [PackNameProblem.Taken], "kot_divan_3"));
    }

    [Fact]
    public async Task Takes_a_name_on_the_list_as_taken_without_asking_the_service()
    {
        await WithKnownAsync();
        Taken("kot_divan_2_by_StickerSnapAppBot", true);
        Taken("kot_divan_3_by_StickerSnapAppBot", false);

        var check = await Service.CheckNameAsync("Кот", "kot_divan", Cancel);

        check.Problems.Should().Equal(PackNameProblem.Taken);
        check.Suggestion.Should().Be("kot_divan_3");
        await _telegram.DidNotReceive().IsNameTakenAsync(Cats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Asks_telegram_nothing_for_a_name_it_would_refuse()
    {
        var check = await Service.CheckNameAsync("Кот", "2kota", Cancel);

        check.Problems.Should().Equal(PackNameProblem.BadStart);
        check.Suggestion.Should().BeNull();
        _telegram.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Offers_nothing_when_every_variant_is_taken()
    {
        _telegram.IsNameTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var check = await Service.CheckNameAsync("Кот", "kot", Cancel);

        check.Suggestion.Should().BeNull();
        await _telegram.Received(PackService.MaxVariant).IsNameTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Records_a_sent_sticker_with_its_project_and_announces_the_pack()
    {
        await WithKnownAsync();
        var after = InTelegram(new("F1", "U1", "😂"), new("F2", "U2", "😴"), new("F3", "U3", "👍"), new("F4", "U4", "🔥"));
        var origin = new StickerOrigin(CatProject, "Кот", "s4");

        var pack = await Service.RecordSentAsync(after, "U4", ["🔥", "🐱"], origin, Cancel);

        pack.Stickers[3].Should().BeEquivalentTo(new PackSticker("U4", "F4", ["🔥", "🐱"], origin));
        (await _packs.FindAsync(Cats, Cancel)).Should().BeEquivalentTo(pack);
        PackEvents.Sent(_events).Should().ContainSingle().Which.Should().BeEquivalentTo(pack);
    }

    [Fact]
    public async Task Adds_a_pack_a_publication_made_to_the_registry()
    {
        var made = InTelegram(new PublishedSticker("F1", "U1", "🐱"));

        await Service.RecordSentAsync(made, "U1", ["🐱"], new StickerOrigin(CatProject, "Кот", "s1"), Cancel);

        _packs.Packs.Should().ContainSingle().Which.Name.Should().Be(Cats);
    }
}
