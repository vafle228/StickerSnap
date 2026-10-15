using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;

namespace StickerSnap.Tests.Core.Packs;

/// <summary>A check with Telegram against what the registry knows of a pack.</summary>
public sealed class PackTests
{
    private const string Name = "kot_divan_by_StickerSnapAppBot";

    private static readonly DateTimeOffset Before = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly StickerOrigin FromCat = new(Guid.Parse("6f1d2c3a-1b2c-4d5e-8f90-0a1b2c3d4e51"), "Кот", "s1");

    private static readonly Pack Known = new(Name, "Кот и диван", PackKind.Regular, PresenceStatus.Ok, Before,
    [
        new PackSticker("U1", "F1", ["😂", "🐱"], FromCat),
        new PackSticker("U2", "F2", ["😴"], FromCat with { SegmentId = "s2" }),
    ]);

    private static PublishedPack Published(string title, params PublishedSticker[] stickers) =>
        new(Name, title, PackKind.Regular, stickers);

    [Fact]
    public void Keeps_the_project_and_all_the_emoji_of_a_sticker_telegram_still_has()
    {
        var checkedPack = Known.Reconciled(Published("Кот и диван", new("F1b", "U1", "😂"), new("F2", "U2", "😴")), Now);

        checkedPack.Should().BeEquivalentTo(Known with
        {
            CheckedAt = Now,
            Stickers = [Known.Stickers[0] with { FileId = "F1b" }, Known.Stickers[1]],
        });
    }

    [Fact]
    public void Drops_a_sticker_deleted_in_telegram_and_adds_one_sent_from_elsewhere_in_telegrams_order()
    {
        var checkedPack = Known.Reconciled(Published("Кот на диване", new("F3", "U3", "👍"), new("F1", "U1", "😂")), Now);

        checkedPack.Title.Should().Be("Кот на диване");
        checkedPack.Stickers.Should().BeEquivalentTo(
            [new PackSticker("U3", "F3", ["👍"], null), Known.Stickers[0]],
            options => options.WithStrictOrdering());
        checkedPack.Count.Should().Be(2);
    }

    [Fact]
    public void Takes_telegrams_emoji_once_they_were_changed_there()
    {
        var checkedPack = Known.Reconciled(Published("Кот и диван", new("F1", "U1", "🔥"), new("F2", "U2", null)), Now);

        checkedPack.Stickers[0].Emoji.Should().Equal("🔥");
        checkedPack.Stickers[0].Origin.Should().Be(FromCat);
        checkedPack.Stickers[1].Emoji.Should().Equal("😴");
    }

    [Fact]
    public void Calls_a_pack_telegram_no_longer_has_missing_and_keeps_its_stickers()
    {
        var checkedPack = Known.Reconciled(null, Now);

        checkedPack.Should().BeEquivalentTo(Known with { Status = PresenceStatus.Missing, CheckedAt = Now });
    }

    [Fact]
    public void Comes_back_when_telegram_has_the_pack_again()
    {
        var gone = Known.Reconciled(null, Before);

        gone.Reconciled(Published("Кот и диван", new PublishedSticker("F1", "U1", "😂")), Now).Status.Should().Be(PresenceStatus.Ok);
    }

    [Fact]
    public void Makes_a_new_pack_of_what_telegram_has()
    {
        var pack = Pack.Of(new PublishedPack(Name, "Эмодзи", PackKind.CustomEmoji, [new("F1", "U1", "😂")]), Now);

        pack.Should().BeEquivalentTo(new Pack(Name, "Эмодзи", PackKind.CustomEmoji, PresenceStatus.Ok, Now, [new PackSticker("U1", "F1", ["😂"], null)]));
        pack.Limit.Should().Be(200);
        Known.Limit.Should().Be(120);
    }

    [Fact]
    public void Leaves_out_a_deleted_sticker()
    {
        Known.Without("U1").Stickers.Should().BeEquivalentTo([Known.Stickers[1]]);
        Known.Without("U9").Stickers.Should().HaveCount(2);
        Known.Find("U2").Should().Be(Known.Stickers[1]);
        Known.Find("U9").Should().BeNull();
    }

    [Fact]
    public void Keeps_a_sent_sticker_with_its_project_and_every_emoji_it_was_sent_with()
    {
        var after = Published("Кот и диван", new("F1", "U1", "😂"), new("F2", "U2", "😴"), new("F3", "U3", "🔥"));

        var sent = Pack.Sent(Known, after, "U3", ["🔥", "😂"], FromCat with { SegmentId = "s3" }, Now);

        sent.Stickers.Should().HaveCount(3);
        sent.Stickers[2].Should().BeEquivalentTo(new PackSticker("U3", "F3", ["🔥", "😂"], FromCat with { SegmentId = "s3" }));
        sent.Stickers[0].Emoji.Should().Equal("😂", "🐱");
        sent.CheckedAt.Should().Be(Now);
    }

    [Fact]
    public void Adds_a_pack_a_publication_made()
    {
        var made = Published("Новый", new PublishedSticker("F9", "U9", "🐱"));

        var sent = Pack.Sent(null, made, "U9", ["🐱"], FromCat, Now);

        sent.Should().BeEquivalentTo(new Pack(Name, "Новый", PackKind.Regular, PresenceStatus.Ok, Now,
            [new PackSticker("U9", "F9", ["🐱"], FromCat)]));
    }

    [Fact]
    public void Finds_the_sticker_a_send_put_into_the_pack()
    {
        var before = Published("Кот", new("F1", "U1", "😂"), new("F2", "U2", "😴"));
        var replaced = Published("Кот", new("F1", "U1", "😂"), new("F5", "U5", "🔥"));
        var added = Published("Кот", new("F1", "U1", "😂"), new("F2", "U2", "😴"), new("F6", "U6", "🔥"));
        var same = Published("Кот", new("F1", "U1", "😂"), new("F2b", "U2", "😴"));

        replaced.SentSince(before, position: 1)!.UniqueId.Should().Be("U5");
        added.SentSince(before)!.UniqueId.Should().Be("U6");
        added.SentSince(null)!.UniqueId.Should().Be("U6");
        same.SentSince(before, position: 0)!.UniqueId.Should().Be("U1");
        same.SentSince(before)!.UniqueId.Should().Be("U2");
    }
}
