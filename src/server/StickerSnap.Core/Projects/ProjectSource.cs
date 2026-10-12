using StickerSnap.Core.Media;

namespace StickerSnap.Core.Projects;

/// <summary>
/// The video a project was made from, as its .ssproj remembers it: where it was, and what the
/// media engine read from it, so the project opens and shows its stickers when the file is gone.
/// </summary>
/// <param name="Path">Absolute, on the machine that saved the project</param>
/// <param name="Fingerprint">
/// A mark of the file's content, compared to tell whether the file at <paramref name="Path"/> is
/// still the one the project was made from. Its form is the server's own; the SPA never sees it.
/// </param>
public sealed record ProjectSource(string Path, string FileName, string Fingerprint, MediaInfo Media);
