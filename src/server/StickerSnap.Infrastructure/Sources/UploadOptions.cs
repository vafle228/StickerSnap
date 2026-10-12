namespace StickerSnap.Infrastructure.Sources;

/// <summary>StickerSnap__Uploads__*: how much a browser may upload.</summary>
public sealed class UploadOptions
{
    public const string Section = "StickerSnap:Uploads";

    /// <summary>The largest file POST /api/v1/uploads takes: 1 GiB, far more than a sticker's source needs.</summary>
    public long MaxBytes { get; set; } = 1L << 30;
}
