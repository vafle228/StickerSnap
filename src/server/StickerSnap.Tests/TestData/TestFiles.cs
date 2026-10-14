using System.Reflection;

namespace StickerSnap.Tests.TestData;

/// <summary>
/// The files under TestData/, embedded in the test assembly as resources named by their path
/// there: `Ffprobe/gif.json`, `Corpus/01-cat.gif`. Tools that need a file on disk, such as
/// FFmpeg, get a copy in a directory of the test's choosing.
/// </summary>
public static class TestFiles
{
    private const string Prefix = "TestData/";

    private static readonly Assembly Assembly = typeof(TestFiles).Assembly;

    /// <summary>The files of a folder of TestData/, as paths relative to TestData/, in name order.</summary>
    public static IReadOnlyList<string> In(string folder) => Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith($"{Prefix}{folder}/", StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..])
        .Order(StringComparer.Ordinal)
        .ToList();

    public static string ReadText(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }

    public static byte[] ReadBytes(string path)
    {
        using var stream = Open(path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Copies <paramref name="path"/> into <paramref name="directory"/> under its own name.</summary>
    /// <returns>The full path of the copy</returns>
    public static async Task<string> CopyToAsync(string path, string directory, CancellationToken cancellationToken)
    {
        var copy = Path.Combine(directory, Path.GetFileName(path));
        await using var source = Open(path);
        await using var target = File.Create(copy);
        await source.CopyToAsync(target, cancellationToken);
        return copy;
    }

    private static Stream Open(string path) =>
        Assembly.GetManifestResourceStream(Prefix + path)
        ?? throw new FileNotFoundException($"No TestData/{path} among the test assembly's resources");
}
