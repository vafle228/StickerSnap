namespace StickerSnap.Tests;

/// <summary>
/// Keeps every progress report in order, as it comes: <see cref="Progress{T}"/> would post them
/// to the thread pool and a test could read the list before they land.
/// </summary>
public sealed class CollectProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_reports) return [.. _reports];
        }
    }

    public void Report(T value)
    {
        lock (_reports) _reports.Add(value);
    }
}
