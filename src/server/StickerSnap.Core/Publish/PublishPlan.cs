using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Core.Publish;

/// <summary>
/// What a publication will do in Telegram, step by step, and what Telegram would refuse before
/// anything is sent: the pack's kind against the segments' presets, its room, the emoji of each
/// sticker, the sticker a new version takes the place of. A new pack is made by its first sticker,
/// the others are added to it; a replaced sticker keeps its position.
/// </summary>
/// <param name="NewPack">The pack does not exist yet: the first sticker that goes makes it</param>
/// <param name="Steps">One per segment, in the order asked for</param>
/// <param name="Problems">Empty when the publication may go</param>
public sealed record PublishPlan(
    PackKind Kind,
    bool NewPack,
    IReadOnlyList<PublishStep> Steps,
    IReadOnlyList<PublishPlanProblem> Problems)
{
    /// <summary>A sticker answers to one emoji at least and to twenty at most.</summary>
    public const int MaxEmoji = 20;

    public bool IsValid => Problems.Count == 0;

    /// <summary>A publication into <paramref name="pack"/>, which the registry has.</summary>
    public static PublishPlan Into(Pack pack, IReadOnlyList<PublishEntry> entries)
    {
        var problems = new List<PublishPlanProblem>();
        if (pack.Status == PresenceStatus.Missing) problems.Add(new PublishPlanProblem(null, PublishProblem.PackMissing));

        var steps = entries.Select((entry, index) =>
        {
            if (entry.Replace is not { } replace) return new PublishStep(entry.SegmentId, PublishMove.Add, entry.Emoji, null);

            var inPack = string.Equals(replace.Pack, pack.Name, StringComparison.OrdinalIgnoreCase) && pack.Find(replace.StickerId) is not null;
            if (!inPack) problems.Add(new PublishPlanProblem(index, PublishProblem.NotInPack));
            return new PublishStep(entry.SegmentId, PublishMove.Replace, entry.Emoji, replace.StickerId);
        }).ToList();

        problems.AddRange(EntryProblems(pack.Kind, entries));
        if (pack.Count + steps.Count(step => step.Move == PublishMove.Add) > pack.Limit)
            problems.Add(new PublishPlanProblem(null, PublishProblem.PackFull));
        return new PublishPlan(pack.Kind, false, steps, problems);
    }

    /// <summary>A publication that makes a new pack of <paramref name="kind"/>.</summary>
    public static PublishPlan New(PackKind kind, IReadOnlyList<PublishEntry> entries)
    {
        var problems = new List<PublishPlanProblem>();
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].Replace is not null) problems.Add(new PublishPlanProblem(i, PublishProblem.NotInPack));

        problems.AddRange(EntryProblems(kind, entries));
        if (entries.Count > CapacityOf(kind)) problems.Add(new PublishPlanProblem(null, PublishProblem.PackFull));
        return new PublishPlan(
            kind,
            true,
            [.. entries.Select(entry => new PublishStep(entry.SegmentId, PublishMove.Add, entry.Emoji, null))],
            problems);
    }

    /// <summary>The kind of pack segments of these presets go to: custom emoji when all of them are emoji.</summary>
    public static PackKind KindFor(IEnumerable<SegmentPreset> presets)
    {
        var all = presets.ToList();
        return all.Count > 0 && all.All(preset => preset == SegmentPreset.Emoji) ? PackKind.CustomEmoji : PackKind.Regular;
    }

    /// <summary>The kind of pack a segment of <paramref name="preset"/> may go to: 512 px stickers or 100×100 emoji.</summary>
    public static PackKind KindOf(SegmentPreset preset) => preset == SegmentPreset.Emoji ? PackKind.CustomEmoji : PackKind.Regular;

    /// <summary>
    /// Why the publish window cannot pick <paramref name="pack"/> for stickers going to a pack of
    /// <paramref name="kind"/>: gone from Telegram, of the other kind, or with no room left; null when it can.
    /// </summary>
    public static PackRefusal? RefusalOf(Pack pack, PackKind kind) => pack switch
    {
        { Status: PresenceStatus.Missing } => PackRefusal.Missing,
        _ when pack.Kind != kind => PackRefusal.WrongType,
        _ when pack.Count >= pack.Limit => PackRefusal.Full,
        _ => null,
    };

    private static int CapacityOf(PackKind kind) => kind == PackKind.CustomEmoji
        ? Telegram.TelegramLimits.Current.EmojiPackCapacity
        : Telegram.TelegramLimits.Current.RegularPackCapacity;

    private static IEnumerable<PublishPlanProblem> EntryProblems(PackKind kind, IReadOnlyList<PublishEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var emoji = entries[i].Emoji;
            if (emoji.Count is 0 or > MaxEmoji || emoji.Any(string.IsNullOrWhiteSpace))
                yield return new PublishPlanProblem(i, PublishProblem.EmojiCount);
            if (KindOf(entries[i].Preset) != kind) yield return new PublishPlanProblem(i, PublishProblem.WrongType);
        }
    }
}

/// <summary>A segment to publish, as the plan needs it.</summary>
/// <param name="Replace">The sticker it takes the place of, keeping its position</param>
public sealed record PublishEntry(string SegmentId, SegmentPreset Preset, IReadOnlyList<string> Emoji, StickerRef? Replace);

/// <summary>What happens to one segment in Telegram.</summary>
/// <param name="OldStickerId">For <see cref="PublishMove.Replace"/>: the file_unique_id of the sticker it replaces</param>
public sealed record PublishStep(string SegmentId, PublishMove Move, IReadOnlyList<string> Emoji, string? OldStickerId);

public enum PublishMove
{
    /// <summary>A new sticker at the end of the pack, or the first one of a new pack</summary>
    Add,

    /// <summary>A new version in the place of an old sticker</summary>
    Replace,
}

/// <param name="Index">The segment's place in the request; null for the pack as a whole</param>
public sealed record PublishPlanProblem(int? Index, PublishProblem Problem);

public enum PublishProblem
{
    /// <summary>Not 1 to <see cref="PublishPlan.MaxEmoji"/> emoji</summary>
    EmojiCount,

    /// <summary>A sticker for an emoji pack, or an emoji for a sticker pack</summary>
    WrongType,

    /// <summary>The sticker to replace is not in the pack</summary>
    NotInPack,

    /// <summary>The new stickers do not fit in the pack</summary>
    PackFull,

    /// <summary>Telegram no longer has the pack</summary>
    PackMissing,
}

/// <summary>Why the publish window shows a pack it cannot pick (contract, PublishDraft.packs.reason).</summary>
public enum PackRefusal
{
    Full,
    WrongType,
    Missing,
}
