using System.Text.Json.Serialization;

namespace StickerSnap.Api.Contracts;

/// <summary>POST /api/v1/validate and later POST /api/v1/projects (schema SourceRequest).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceRequest(SourceRef Source);

/// <summary>Schema SourceRef: a path on disk from the desktop app, or an upload from a browser, told by `kind`.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PathSource), "path")]
[JsonDerivedType(typeof(UploadSource), "upload")]
public abstract record SourceRef
{
    public abstract Core.Media.SourceRef ToSource();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PathSource(string Path) : SourceRef
{
    public override Core.Media.SourceRef ToSource() => new Core.Media.SourceRef.OnDisk(Path);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UploadSource(string UploadId) : SourceRef
{
    public override Core.Media.SourceRef ToSource() => new Core.Media.SourceRef.Upload(UploadId);
}
