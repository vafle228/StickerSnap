using StickerSnap.Application.Sources;

namespace StickerSnap.Application.Projects;

/// <summary>The names a new project's file may take in the projects folder, after its video.</summary>
public static class ProjectFileNames
{
    public const string Extension = ".ssproj";

    /// <summary>
    /// `cat.ssproj`, then `cat (2).ssproj`, `cat (3).ssproj`… for a project named `cat`: the store
    /// takes the first that is free, so a new project never overwrites another.
    /// </summary>
    public static IEnumerable<string> Candidates(string projectName)
    {
        var stem = SafeFileNames.Of(projectName);
        yield return stem + Extension;
        for (var n = 2; ; n++) yield return $"{stem} ({n}){Extension}";
    }

    /// <summary>The name a project made from <paramref name="sourceFileName"/> gets: the video's name without its extension.</summary>
    public static string ProjectNameOf(string sourceFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceFileName).Trim();
        return stem.Length > 0 ? stem : sourceFileName;
    }
}
