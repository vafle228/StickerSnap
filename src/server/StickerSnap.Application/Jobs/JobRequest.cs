using StickerSnap.Core.Jobs;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Jobs;

/// <summary>What POST /api/v1/jobs asks for (contract, JobRequest): a job over segments of one project.</summary>
public abstract record JobRequest(Guid ProjectId)
{
    public abstract JobKind Kind { get; }

    /// <summary>The segments, one item of the job each, in this order.</summary>
    public abstract IReadOnlyList<string> SegmentIds { get; }

    /// <summary>«Точное превью» (3c D): a draft of the segment as the editor has it, saved or not.</summary>
    public sealed record Preview(Guid ProjectId, Segment Segment) : JobRequest(ProjectId)
    {
        public override JobKind Kind => JobKind.Preview;

        public override IReadOnlyList<string> SegmentIds => [Segment.Id];
    }

    /// <summary>«Экспорт WEBM» of saved segments, by the revision the SPA saved last.</summary>
    public sealed record Export(Guid ProjectId, int Revision, IReadOnlyList<string> SegmentIds) : JobRequest(ProjectId)
    {
        public override JobKind Kind => JobKind.Export;

        public override IReadOnlyList<string> SegmentIds { get; } = SegmentIds;
    }

    /// <summary>Saved segments sent to a pack, by the revision the SPA saved last.</summary>
    public sealed record Publish(Guid ProjectId, int Revision, IReadOnlyList<PublishItem> Items, PublishTarget Target)
        : JobRequest(ProjectId)
    {
        public override JobKind Kind => JobKind.Publish;

        public override IReadOnlyList<string> SegmentIds => [.. Items.Select(item => item.SegmentId)];
    }
}

/// <summary>A segment to publish, with its emoji (1–20).</summary>
/// <param name="Replace">The sticker it takes the place of, keeping its position</param>
public sealed record PublishItem(string SegmentId, IReadOnlyList<string> Emoji, StickerRef? Replace);

/// <summary>A sticker in a pack (contract, StickerRef).</summary>
public sealed record StickerRef(string Pack, string StickerId);

/// <summary>Where a publication goes: a pack of the user's or a new one.</summary>
public abstract record PublishTarget
{
    private PublishTarget()
    {
    }

    public sealed record Existing(string Pack) : PublishTarget;

    /// <param name="Name">The link, without _by_ and the bot's name</param>
    public sealed record New(string Title, string Name, PackKind Type) : PublishTarget;
}
