namespace StickerSnap.Core.Projects;

/// <summary>What a scene is drawn from, told apart by `kind` in the contract (VideoLayer).</summary>
public abstract record Layer
{
    private Layer(string id) => Id = id;

    public string Id { get; }

    /// <summary>The source video itself.</summary>
    public sealed record Video(string Id) : Layer(Id);
}
