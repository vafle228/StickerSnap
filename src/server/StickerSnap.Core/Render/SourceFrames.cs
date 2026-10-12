namespace StickerSnap.Core.Render;

/// <summary>
/// The frames of the source a sticker plays, by their numbers in the source: from
/// <paramref name="First"/> to <paramref name="Last"/>, both included, less the excluded ones.
/// </summary>
/// <param name="Excluded">Between First and Last, in ascending order, each once (P1)</param>
public sealed record SourceFrames(int First, int Last, IReadOnlyList<int> Excluded)
{
    /// <summary>The frames that play.</summary>
    public int Count => Last - First + 1 - Excluded.Count;

    /// <summary>The frames that play, as runs of consecutive numbers: 0–2 and 4–9 for 0–9 without 3.</summary>
    public IReadOnlyList<FrameRun> Runs()
    {
        List<FrameRun> runs = [];
        var start = First;
        foreach (var excluded in Excluded)
        {
            if (excluded > start) runs.Add(new FrameRun(start, excluded - 1));
            start = excluded + 1;
        }

        if (start <= Last) runs.Add(new FrameRun(start, Last));
        return runs;
    }

    /// <summary>The first <paramref name="count"/> frames that play.</summary>
    public SourceFrames Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (count >= Count) return this;

        var last = First + count - 1;
        foreach (var excluded in Excluded)
        {
            if (excluded > last) break;
            last++;
        }

        return new SourceFrames(First, last, Excluded.Where(frame => frame < last).ToList());
    }

    /// <summary>The same frames, whichever list holds the excluded ones.</summary>
    public bool Equals(SourceFrames? other) =>
        other is not null && First == other.First && Last == other.Last && Excluded.SequenceEqual(other.Excluded);

    public override int GetHashCode() => HashCode.Combine(First, Last, Excluded.Count);
}

/// <summary>Frames <paramref name="First"/> to <paramref name="Last"/> of the source, both included.</summary>
public readonly record struct FrameRun(int First, int Last);
