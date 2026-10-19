using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>The command line a bug's report shows: what ran, as a shell reads it back.</summary>
public sealed class FfmpegProcessTests
{
    [Fact]
    public void Names_the_program_and_quotes_the_arguments_a_shell_would_split()
    {
        var line = FfmpegProcess.CommandLine(
            Path.Combine(Path.GetTempPath(), "Stick Snap", FfmpegProcess.Ffmpeg), ["-i", @"C:\Видео\cat dog.gif", "-vf", "scale=512:-2,fps=30", "-metadata", "title=\"a\""]);

        line.Should().Be("""ffmpeg -i "C:\Видео\cat dog.gif" -vf scale=512:-2,fps=30 -metadata "title=\"a\"" """.TrimEnd());
    }
}
