using StickerSnap.Application.Jobs;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Publish;

namespace StickerSnap.Application.Publish;

/// <summary>
/// The plan of a publish request against the saved project and the registry of packs, checked
/// before the job is queued, before «Повторить», and when the job starts.
/// </summary>
internal static class PublishPlans
{
    /// <summary>What Telegram allows for a pack's title.</summary>
    private const int PackTitleMax = 64;

    /// <summary>
    /// The plan for the items of <paramref name="request"/> named in <paramref name="segmentIds"/>,
    /// whose segments are in <paramref name="project"/>. A new pack that the registry has already is
    /// the one an earlier run of this job made: the rest goes into it.
    /// </summary>
    /// <param name="known">The registry's pack of the target's name, if any</param>
    /// <exception cref="AppException">
    /// pack_not_found for a pack the registry or Telegram does not have; pack_name_invalid for a
    /// new pack's name Telegram would refuse; pack_full; bad_request for an item Telegram would refuse
    /// </exception>
    public static PublishPlan For(
        JobRequest.Publish request, IReadOnlyCollection<string> segmentIds, Project project, Pack? known)
    {
        var entries = new List<PublishEntry>();
        var indexes = new List<int>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (!segmentIds.Contains(item.SegmentId)) continue;
            var segment = project.Scene.Segments.First(segment => segment.Id == item.SegmentId);
            entries.Add(new PublishEntry(item.SegmentId, segment.Preset, item.Emoji, item.Replace));
            indexes.Add(i);
        }

        var plan = (request.Target, known) switch
        {
            (_, { } pack) => PublishPlan.Into(pack, entries),
            (PublishTarget.Existing existing, null) =>
                throw new AppException(ErrorCode.PackNotFound, $"The registry has no pack {existing.Pack}"),
            (PublishTarget.New target, null) => NewPlan(target, entries),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Target, "An unknown publish target"),
        };
        Ensure(plan, indexes, request.Target.PackName);
        return plan;
    }

    private static PublishPlan NewPlan(PublishTarget.New target, IReadOnlyList<PublishEntry> entries)
    {
        var problems = PackNames.Check(target.Name);
        if (problems.Count > 0)
            throw new AppException(ErrorCode.PackNameInvalid, $"Telegram would refuse the pack name {target.Name}")
            {
                Fields = new Dictionary<string, string[]>
                {
                    ["target.name"] = [.. problems.Select(problem => Snake(problem.ToString()))],
                },
            };
        if (string.IsNullOrWhiteSpace(target.Title) || target.Title.Length > PackTitleMax)
            throw new AppException(ErrorCode.BadRequest, "A new pack needs a title of 1 to 64 characters")
            {
                Fields = new Dictionary<string, string[]> { ["target.title"] = [target.Title.Length > 0 ? "too_long" : "empty"] },
            };
        return PublishPlan.New(target.Type, entries);
    }

    /// <param name="indexes">The place in the request of each entry of the plan</param>
    private static void Ensure(PublishPlan plan, IReadOnlyList<int> indexes, string pack)
    {
        if (plan.IsValid) return;

        if (plan.Problems.Any(problem => problem.Problem == PublishProblem.PackMissing))
            throw new AppException(ErrorCode.PackNotFound, $"Telegram no longer has the pack {pack}");
        if (plan.Problems.Any(problem => problem.Problem == PublishProblem.PackFull))
            throw new AppException(ErrorCode.PackFull, $"The stickers do not fit in the pack {pack}");

        var fields = plan.Problems
            .Where(problem => problem.Index is not null)
            .GroupBy(problem => problem.Problem switch
            {
                PublishProblem.EmojiCount => $"items[{indexes[problem.Index!.Value]}].emoji",
                PublishProblem.NotInPack => $"items[{indexes[problem.Index!.Value]}].replace",
                _ => $"items[{indexes[problem.Index!.Value]}]",
            })
            .ToDictionary(
                group => group.Key,
                group => group.Select(problem => problem.Problem switch
                {
                    PublishProblem.EmojiCount => "count",
                    PublishProblem.WrongType => "wrong_type",
                    _ => "not_in_pack",
                }).ToArray());
        throw new AppException(ErrorCode.BadRequest, $"Telegram would refuse {fields.Count} item(s) of the publication") { Fields = fields };
    }

    private static string Snake(string name) =>
        string.Concat(name.Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}
