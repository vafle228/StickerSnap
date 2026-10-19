using Microsoft.Extensions.Logging;
using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Diagnostics;

/// <summary>
/// The last <see cref="Capacity"/> reportable errors in memory, until the server restarts. An
/// expected error, which the user mends alone, is not kept: it would push out the ones a report needs.
/// </summary>
public sealed class ErrorJournal(ILogger<ErrorJournal> logger) : IErrorJournal
{
    public const int Capacity = 50;

    private readonly Lock _gate = new();
    private readonly Queue<ErrorRecord> _records = new(Capacity);

    /// <inheritdoc />
    public void Record(ErrorRecord record)
    {
        if (!record.Code.IsReportable()) return;
        lock (_gate)
        {
            if (_records.Count == Capacity) _records.Dequeue();
            _records.Enqueue(record);
        }
        logger.LogWarning(
            "{Code} in {Operation}, trace {TraceId}: {Message}", record.Code, record.Operation, record.TraceId, record.Message);
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorRecord> Find(string traceId)
    {
        lock (_gate) return [.. _records.Where(record => record.TraceId == traceId)];
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorRecord> Latest(int count)
    {
        lock (_gate) return [.. _records.Reverse().Take(count)];
    }
}
