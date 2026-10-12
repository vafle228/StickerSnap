namespace StickerSnap.Core.Render;

/// <summary>What the fit ladder gave up to bring a sticker under the size limit: «Чем пожертвовали» (R7).</summary>
public abstract record Concession
{
    private Concession()
    {
    }

    /// <summary>Fewer frames a second.</summary>
    public sealed record Fps(int From, int To) : Concession
    {
        public override string ToString() => $"fps {From} → {To}";
    }

    /// <summary>A coarser picture: the constant quality every frame is held to, higher is worse.</summary>
    public sealed record Crf(int From, int To) : Concession
    {
        public override string ToString() => $"crf {From} → {To}";
    }

    /// <summary>
    /// Fewer details: the picture is shrunk to <paramref name="To"/> px on its longer side and
    /// brought back to the sticker's size, which keeps its exact side.
    /// </summary>
    public sealed record Scale(int From, int To) : Concession
    {
        public override string ToString() => $"scale {From} → {To}";
    }
}
