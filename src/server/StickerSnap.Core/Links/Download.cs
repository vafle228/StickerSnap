using StickerSnap.Core.Jobs;

namespace StickerSnap.Core.Links;

/// <summary>Where a download is (contract, Download.state).</summary>
public enum DownloadState
{
    Queued,
    Downloading,
    Reading,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>
/// A video downloaded by link to become a project (contract, Download, artboards 20·3·5–20·4).
/// A snapshot: every move gives a new one. The moves are
/// <c>queued → downloading → reading → succeeded</c>; anything not over may fail or be canceled;
/// any other move throws.
/// </summary>
/// <param name="EtaSec">How long is left, while downloading and when the site tells</param>
/// <param name="ProjectId">Once it succeeded: the project made of the video</param>
/// <param name="Error">Why it failed</param>
public sealed record Download(
    string Id,
    LinkVideo Video,
    DownloadState State,
    long ReceivedBytes,
    DateTimeOffset CreatedAt,
    double? EtaSec = null,
    Guid? ProjectId = null,
    JobError? Error = null)
{
    public static Download Queue(string id, LinkVideo video, DateTimeOffset createdAt) =>
        new(id, video, DownloadState.Queued, 0, createdAt);

    /// <summary>Succeeded, failed or canceled: it will not move again.</summary>
    public bool IsOver => State is DownloadState.Succeeded or DownloadState.Failed or DownloadState.Canceled;

    /// <summary>queued → downloading.</summary>
    public Download Start()
    {
        Ensure(State == DownloadState.Queued, "start");
        return this with { State = DownloadState.Downloading };
    }

    /// <summary>More of the file came while downloading; the bytes never go back.</summary>
    public Download Receive(long receivedBytes, double? etaSec)
    {
        Ensure(State == DownloadState.Downloading, "receive");
        return this with { ReceivedBytes = Math.Max(ReceivedBytes, receivedBytes), EtaSec = etaSec };
    }

    /// <summary>downloading → reading: the whole file is there and is read as a new project's video.</summary>
    public Download Read(long receivedBytes)
    {
        Ensure(State == DownloadState.Downloading, "read");
        return this with { State = DownloadState.Reading, ReceivedBytes = receivedBytes, EtaSec = null };
    }

    /// <summary>reading → succeeded with the project made.</summary>
    public Download Succeed(Guid projectId)
    {
        Ensure(State == DownloadState.Reading, "succeed");
        return this with { State = DownloadState.Succeeded, ProjectId = projectId };
    }

    public Download Fail(JobError error)
    {
        Ensure(!IsOver, "fail");
        return this with { State = DownloadState.Failed, Error = error, EtaSec = null };
    }

    public Download Cancel()
    {
        Ensure(!IsOver, "cancel");
        return this with { State = DownloadState.Canceled, EtaSec = null };
    }

    private void Ensure(bool allowed, string move)
    {
        if (!allowed) throw new InvalidOperationException($"A {State.ToString().ToLowerInvariant()} download cannot {move}");
    }
}
