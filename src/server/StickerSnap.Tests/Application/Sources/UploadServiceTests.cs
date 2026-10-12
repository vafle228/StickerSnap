using System.Text;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;

namespace StickerSnap.Tests.Application.Sources;

/// <summary>A browser's file: its format told by the content, its name made safe, its bytes passed on whole.</summary>
public sealed class UploadServiceTests
{
    private static readonly byte[] Gif = [.. Encoding.ASCII.GetBytes("GIF89a"), .. Enumerable.Range(0, 200).Select(i => (byte)i)];

    private readonly IUploadStore _store = Substitute.For<IUploadStore>();
    private readonly List<(string Name, byte[] Bytes)> _saved = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public UploadServiceTests()
    {
        _store.SaveAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                using var rest = new MemoryStream();
                await call.Arg<Stream>().CopyToAsync(rest);
                _saved.Add((call.Arg<string>(), [.. call.Arg<ReadOnlyMemory<byte>>().ToArray(), .. rest.ToArray()]));
                return "u1";
            });
    }

    private Task<string> UploadAsync(string name, byte[] content) =>
        new UploadService(_store).UploadAsync(name, new MemoryStream(content), Cancel);

    [Fact]
    public async Task Passes_the_whole_file_on_and_gives_the_store_s_id()
    {
        var id = await UploadAsync("cat.gif", Gif);

        id.Should().Be("u1");
        _saved.Single().Name.Should().Be("cat.gif");
        _saved.Single().Bytes.Should().Equal(Gif);
    }

    [Fact]
    public async Task Names_the_file_after_its_content_not_its_extension()
    {
        await UploadAsync("cat.mp4", Gif);

        _saved.Single().Name.Should().Be("cat.gif");
    }

    [Theory]
    [InlineData(@"C:\Users\u\Видео\cat.gif", "cat.gif")]
    [InlineData("../../etc/cat.gif", "cat.gif")]
    [InlineData("кот: «вечер»?.gif", "кот_ «вечер»_.gif")]
    [InlineData("", "video.gif")]
    [InlineData(".gif", "video.gif")]
    public async Task Keeps_a_safe_name_of_one_file(string name, string saved)
    {
        await UploadAsync(name, Gif);

        _saved.Single().Name.Should().Be(saved);
    }

    [Fact]
    public async Task Refuses_content_the_editor_does_not_open_with_the_extension_of_its_name()
    {
        var upload = () => UploadAsync("clip.AVI", Encoding.ASCII.GetBytes("RIFF\0\0\0\0AVI LIST"));

        var exception = (await upload.Should().ThrowAsync<AppException>()).Which;
        exception.Code.Should().Be(ErrorCode.FormatUnsupported);
        exception.Params["ext"].Should().Be("avi");
        _saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Takes_a_file_shorter_than_the_bytes_it_looks_at()
    {
        var tiny = Encoding.ASCII.GetBytes("GIF89a");

        await UploadAsync("tiny.gif", tiny);

        _saved.Single().Bytes.Should().Equal(tiny);
    }
}

/// <summary>The names a new project's file and a project itself take after the video.</summary>
public sealed class ProjectFileNamesTests
{
    [Fact]
    public void Tries_the_video_s_name_then_numbers_it()
    {
        ProjectFileNames.Candidates("cat").Take(3).Should().Equal("cat.ssproj", "cat (2).ssproj", "cat (3).ssproj");
    }

    [Theory]
    [InlineData("a/b", "b.ssproj")]
    [InlineData("кот?", "кот_.ssproj")]
    [InlineData("   ", "video.ssproj")]
    public void Makes_a_safe_file_name_of_any_project_name(string name, string first)
    {
        ProjectFileNames.Candidates(name).First().Should().Be(first);
    }

    [Theory]
    [InlineData("cat-keyboard.gif", "cat-keyboard")]
    [InlineData("Мой ролик.final.mp4", "Мой ролик.final")]
    [InlineData(".gif", ".gif")]
    public void Names_a_project_after_its_video_without_the_extension(string fileName, string name)
    {
        ProjectFileNames.ProjectNameOf(fileName).Should().Be(name);
    }
}
