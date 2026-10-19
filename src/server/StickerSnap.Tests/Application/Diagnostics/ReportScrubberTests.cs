using StickerSnap.Application.Diagnostics;

namespace StickerSnap.Tests.Application.Diagnostics;

/// <summary>
/// A report never carries full paths, the system's user name, keys or tokens (artboard 19,
/// «Никогда»). Paths keep their file names, so the developer still sees which file failed.
/// </summary>
public sealed class ReportScrubberTests
{
    private static readonly ReportScrubber Scrubber = new(["ivan"], ["dk_8f2b5c9e1a7d4f3b"]);

    [Theory]
    [InlineData(@"ffprobe could not read C:\Users\Ivan Petrov\Видео\cat dog.mp4 (exit code 1)", "ffprobe could not read cat dog.mp4 (exit code 1)")]
    [InlineData(@"{""path"":""D:\\Projects\\Коты\\cat.ssproj""}", @"{""path"":""cat.ssproj""}")]
    [InlineData("ffmpeg -i C:/Users/ivan/Videos/cat.gif -c:v libvpx-vp9 out.webm", "ffmpeg -i cat.gif -c:v libvpx-vp9 out.webm")]
    [InlineData(@"\\nas\share\videos\cat.mp4 is gone", "cat.mp4 is gone")]
    [InlineData("open /home/ivan/.config/StickerSnap/projects.json", "open projects.json")]
    [InlineData("/Users/ivan/Movies/cat.mov and ~/Downloads/dog.gif", "cat.mov and dog.gif")]
    public void Keeps_only_the_file_name_of_a_path(string text, string scrubbed)
    {
        Scrubber.Scrub(text).Should().Be(scrubbed);
    }

    [Fact]
    public void Leaves_the_routes_of_the_api_as_they_are()
    {
        Scrubber.Scrub("POST /api/v1/projects/{id}/export at 13:22:05").Should().Be("POST /api/v1/projects/{id}/export at 13:22:05");
    }

    [Fact]
    public void Writes_the_user_s_name_as_user_wherever_it_stands_alone()
    {
        Scrubber.Scrub("Ivan opened a project; ivanova is someone else").Should().Be("<user> opened a project; ivanova is someone else");
    }

    [Theory]
    [InlineData("GET https://api.telegram.org/bot123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw/getMe", "GET https://api.telegram.org/bot<secret>/getMe")]
    [InlineData("Authorization: Device dk_live_7Hq2", "Authorization: <secret>")]
    [InlineData(@"{""deviceKey"":""k3y-k3y"",""name"":""PC""}", @"{""deviceKey"":""<secret>"",""name"":""PC""}")]
    [InlineData("?pollToken=pt_9f8e7d&x=1", "?pollToken=<secret>&x=1")]
    [InlineData("the key dk_8f2b5c9e1a7d4f3b turned up", "the key <secret> turned up")]
    public void Writes_keys_and_tokens_as_secret(string text, string scrubbed)
    {
        Scrubber.Scrub(text).Should().Be(scrubbed);
    }
}
