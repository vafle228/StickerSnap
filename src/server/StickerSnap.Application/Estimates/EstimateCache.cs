namespace StickerSnap.Application.Estimates;

/// <summary>Forecasts by the signature of their segment; past the capacity, the one used longest ago goes.</summary>
internal sealed class EstimateCache(int capacity)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, SegmentEstimate Estimate)>> _byKey = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, SegmentEstimate Estimate)> _byUse = new();

    public SegmentEstimate? Find(string key)
    {
        lock (_lock)
        {
            if (!_byKey.TryGetValue(key, out var node)) return null;
            _byUse.Remove(node);
            _byUse.AddFirst(node);
            return node.Value.Estimate;
        }
    }

    public void Add(string key, SegmentEstimate estimate)
    {
        lock (_lock)
        {
            if (_byKey.Remove(key, out var old)) _byUse.Remove(old);
            _byKey[key] = _byUse.AddFirst((key, estimate));
            if (_byKey.Count <= capacity) return;

            _byKey.Remove(_byUse.Last!.Value.Key);
            _byUse.RemoveLast();
        }
    }
}
