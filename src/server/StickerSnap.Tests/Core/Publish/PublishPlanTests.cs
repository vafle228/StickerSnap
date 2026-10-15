using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;

namespace StickerSnap.Tests.Core.Publish;

/// <summary>The plan of a publication: new pack, existing pack, replacement; room, kind and emoji.</summary>
public sealed class PublishPlanTests
{
    private const string Cats = "cats_by_StickerSnapAppBot";

    private static Pack Pack(int count, PackKind kind = PackKind.Regular, PresenceStatus status = PresenceStatus.Ok) =>
        new(Cats, "Коты", kind, status, null, [.. Enumerable.Range(0, count).Select(i => new PackSticker($"U{i}", $"F{i}", ["🐱"], null))]);

    private static PublishEntry Sticker(string id, params string[] emoji) =>
        new(id, SegmentPreset.Sticker, emoji.Length > 0 ? emoji : ["🐱"], null);

    [Fact]
    public void Adds_every_sticker_of_a_new_pack_the_first_one_making_it()
    {
        var plan = PublishPlan.New(PackKind.Regular, [Sticker("s1"), Sticker("s2", "😂", "🔥")]);

        plan.IsValid.Should().BeTrue();
        plan.NewPack.Should().BeTrue();
        plan.Steps.Should().BeEquivalentTo(
            [new PublishStep("s1", PublishMove.Add, ["🐱"], null), new PublishStep("s2", PublishMove.Add, ["😂", "🔥"], null)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Adds_to_an_existing_pack_and_replaces_a_sticker_in_its_place()
    {
        var plan = PublishPlan.Into(Pack(3), [Sticker("s1"), Sticker("s2") with { Replace = new StickerRef(Cats, "U1") }]);

        plan.IsValid.Should().BeTrue();
        plan.NewPack.Should().BeFalse();
        plan.Steps.Select(step => (step.Move, step.OldStickerId)).Should().Equal(
            (PublishMove.Add, null),
            (PublishMove.Replace, "U1"));
    }

    [Theory]
    [InlineData(118, 2, true)]
    [InlineData(119, 2, false)]
    [InlineData(120, 0, true)]
    public void Counts_only_new_stickers_against_the_room(int count, int added, bool fits)
    {
        var replacing = Sticker("r") with { Replace = new StickerRef(Cats, "U0") };
        var entries = Enumerable.Range(0, added).Select(i => Sticker($"s{i}")).Append(replacing).ToList();

        var plan = PublishPlan.Into(Pack(count), entries);

        plan.Problems.Any(problem => problem.Problem == PublishProblem.PackFull).Should().Be(!fits);
    }

    [Fact]
    public void Refuses_more_stickers_than_a_new_pack_holds()
    {
        var entries = Enumerable.Range(0, 201).Select(i => new PublishEntry($"e{i}", SegmentPreset.Emoji, ["🐱"], null)).ToList();

        PublishPlan.New(PackKind.CustomEmoji, entries).Problems.Should().ContainSingle()
            .Which.Should().Be(new PublishPlanProblem(null, PublishProblem.PackFull));
    }

    [Fact]
    public void Refuses_a_sticker_for_an_emoji_pack_and_an_emoji_for_a_sticker_pack()
    {
        var emoji = new PublishEntry("e1", SegmentPreset.Emoji, ["🐱"], null);

        PublishPlan.Into(Pack(1), [Sticker("s1"), emoji]).Problems.Should().Equal(new PublishPlanProblem(1, PublishProblem.WrongType));
        PublishPlan.New(PackKind.CustomEmoji, [Sticker("s1"), emoji]).Problems.Should().Equal(new PublishPlanProblem(0, PublishProblem.WrongType));
    }

    [Fact]
    public void Wants_one_to_twenty_emoji_per_sticker()
    {
        var twenty = Enumerable.Repeat("🐱", 20).ToArray();
        var entries = new[] { Sticker("s1") with { Emoji = [] }, Sticker("s2", twenty), Sticker("s3", [.. twenty, "🐶"]), Sticker("s4", " ") };

        PublishPlan.New(PackKind.Regular, entries).Problems.Should().Equal(
            new PublishPlanProblem(0, PublishProblem.EmojiCount),
            new PublishPlanProblem(2, PublishProblem.EmojiCount),
            new PublishPlanProblem(3, PublishProblem.EmojiCount));
    }

    [Fact]
    public void Replaces_only_a_sticker_of_the_target_pack()
    {
        var elsewhere = Sticker("s1") with { Replace = new StickerRef("dogs_by_StickerSnapAppBot", "U1") };
        var unknown = Sticker("s2") with { Replace = new StickerRef(Cats, "U9") };

        PublishPlan.Into(Pack(3), [elsewhere, unknown]).Problems.Should().Equal(
            new PublishPlanProblem(0, PublishProblem.NotInPack),
            new PublishPlanProblem(1, PublishProblem.NotInPack));
        PublishPlan.New(PackKind.Regular, [Sticker("s3") with { Replace = new StickerRef(Cats, "U1") }]).Problems
            .Should().Equal(new PublishPlanProblem(0, PublishProblem.NotInPack));
    }

    [Fact]
    public void Refuses_a_pack_gone_from_telegram()
    {
        PublishPlan.Into(Pack(3, status: PresenceStatus.Missing), [Sticker("s1")]).Problems
            .Should().Equal(new PublishPlanProblem(null, PublishProblem.PackMissing));
    }

    [Fact]
    public void Picks_an_emoji_pack_only_for_emoji()
    {
        PublishPlan.KindFor([SegmentPreset.Emoji, SegmentPreset.Emoji]).Should().Be(PackKind.CustomEmoji);
        PublishPlan.KindFor([SegmentPreset.Emoji, SegmentPreset.Sticker]).Should().Be(PackKind.Regular);
        PublishPlan.KindFor([]).Should().Be(PackKind.Regular);
    }

    [Fact]
    public void Tells_the_window_why_a_pack_cannot_be_picked()
    {
        PublishPlan.RefusalOf(Pack(3), PackKind.Regular).Should().BeNull();
        PublishPlan.RefusalOf(Pack(120), PackKind.Regular).Should().Be(PackRefusal.Full);
        PublishPlan.RefusalOf(Pack(3, PackKind.CustomEmoji), PackKind.Regular).Should().Be(PackRefusal.WrongType);
        PublishPlan.RefusalOf(Pack(120, status: PresenceStatus.Missing), PackKind.Regular).Should().Be(PackRefusal.Missing);
    }
}
