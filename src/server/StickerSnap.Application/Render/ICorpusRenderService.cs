namespace StickerSnap.Application.Render;

/// <summary>Renders the default sticker of every clip of a corpus in each of the given ways and reads each result back.</summary>
public interface ICorpusRenderService
{
    Task<CorpusReport> RunAsync(
        IReadOnlyList<string> sources,
        string outputDirectory,
        IReadOnlyList<RenderMode> modes,
        IProgress<CorpusEntry>? progress,
        CancellationToken cancellationToken);
}
