using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Infrastructure.Packs;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Packs;

/// <summary>packs.json in a temporary data directory.</summary>
public sealed class JsonPackRepositoryTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-packs-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_data.FullName, JsonPackRepository.FileName);

    public void Dispose() => _data.Delete(recursive: true);

    private JsonPackRepository Repository() =>
        new(Options.Create(new DataOptions { DataDir = _data.FullName }), NullLogger<JsonPackRepository>.Instance);

    private static Pack Cats() => new(
        "kot_divan_by_StickerSnapAppBot",
        "Кот и диван",
        PackKind.Regular,
        PresenceStatus.Ok,
        new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero),
        [
            new PackSticker("AgADc2", "CAACAgIAAxkBAAIBc2", ["😂", "🐱"], new StickerOrigin(Guid.Parse("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51"), "Кот", "s1")),
            new PackSticker("AgADc3", "CAACAgIAAxkBAAIBc3", [], null),
        ]);

    private static Pack Emoji() => new("chat_by_StickerSnapAppBot", "Эмодзи", PackKind.CustomEmoji, PresenceStatus.Missing, null, []);

    [Fact]
    public async Task Has_no_packs_on_a_first_launch()
    {
        (await Repository().ListAsync(Cancel)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_back_what_it_saved_after_a_restart_in_the_order_added()
    {
        var repository = Repository();
        await repository.SaveAsync(Cats(), Cancel);
        await repository.SaveAsync(Emoji(), Cancel);

        var packs = await Repository().ListAsync(Cancel);

        packs.Should().BeEquivalentTo([Cats(), Emoji()], options => options.WithStrictOrdering());
        var text = await File.ReadAllTextAsync(FilePath, Cancel);
        text.Should().Contain("\"fileUniqueId\": \"AgADc2\"").And.Contain("Кот и диван").And.Contain("\"kind\": \"custom_emoji\"");
    }

    [Fact]
    public async Task Finds_and_replaces_a_pack_by_its_name_in_any_case_in_its_place()
    {
        var repository = Repository();
        await repository.SaveAsync(Cats(), Cancel);
        await repository.SaveAsync(Emoji(), Cancel);

        await repository.SaveAsync(Cats() with { Name = "Kot_Divan_by_StickerSnapAppBot", Title = "Кот" }, Cancel);

        (await repository.FindAsync("KOT_DIVAN_BY_STICKERSNAPAPPBOT", Cancel))!.Title.Should().Be("Кот");
        (await Repository().ListAsync(Cancel)).Select(pack => pack.Title).Should().Equal("Кот", "Эмодзи");
    }

    [Fact]
    public async Task Forgets_a_pack()
    {
        var repository = Repository();
        await repository.SaveAsync(Cats(), Cancel);

        await repository.RemoveAsync("kot_divan_by_stickersnapappbot", Cancel);
        await repository.RemoveAsync("unknown_by_StickerSnapAppBot", Cancel);

        (await Repository().ListAsync(Cancel)).Should().BeEmpty();
        (await repository.FindAsync(Cats().Name, Cancel)).Should().BeNull();
    }

    [Fact]
    public async Task Sets_a_broken_file_aside_and_starts_empty()
    {
        await File.WriteAllTextAsync(FilePath, "{ not json", Cancel);

        (await Repository().ListAsync(Cancel)).Should().BeEmpty();
        File.Exists(FilePath + ".bad").Should().BeTrue();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Sets_aside_a_file_of_a_version_it_does_not_know()
    {
        await File.WriteAllTextAsync(FilePath, """{ "version": 2, "packs": [] }""", Cancel);

        (await Repository().ListAsync(Cancel)).Should().BeEmpty();
        File.Exists(FilePath + ".bad").Should().BeTrue();
    }
}
