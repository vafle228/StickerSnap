using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Api;
using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>
/// POST /uploads, POST /projects and PUT …/source over a substituted probe and real files in
/// temporary directories: the import as the SPA drives it, in both modes.
/// </summary>
public sealed class SourcesEndpointTests : IDisposable
{
    private static readonly byte[] Gif = [.. Encoding.ASCII.GetBytes("GIF89a"), .. new byte[100]];

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-sources-");
    private readonly ApiFactory _web = new(("Mode", "web"));
    private readonly ApiFactory _desktop = new(("Mode", "desktop"));
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string ProjectsFolder => Path.Combine(_files.FullName, "Проекты");

    public SourcesEndpointTests()
    {
        _probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Scenes.Source);
    }

    public void Dispose()
    {
        _web.Dispose();
        _desktop.Dispose();
        _files.Delete(recursive: true);
    }

    /// <summary>The server with the substituted probe, its projects folder in the temporary directory.</summary>
    private async Task<HttpClient> ClientAsync(ApiFactory factory)
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(_probe)))
            .CreateClient();
        var folders = new JsonObject { ["folders"] = new JsonObject { ["projects"] = ProjectsFolder } };
        (await client.PatchAsync("/api/v1/settings", Body(folders), Cancel)).EnsureSuccessStatusCode();
        return client;
    }

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static JsonObject SourceBody(string kind, string value) =>
        new() { ["source"] = new JsonObject { ["kind"] = kind, [kind == "path" ? "path" : "uploadId"] = value } };

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync("/api/v1/uploads", form, Cancel);
    }

    private string[] ProjectFiles() =>
        Directory.Exists(ProjectsFolder) ? Directory.GetFiles(ProjectsFolder) : [];

    [Fact]
    public async Task Uploads_a_file_and_makes_a_project_of_it_in_web_mode()
    {
        var client = await ClientAsync(_web);

        var uploaded = await UploadAsync(client, Gif, "cat-keyboard.gif");
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadId = (await Json(uploaded))["uploadId"]!.GetValue<string>();

        var created = await client.PostAsync("/api/v1/projects", Body(SourceBody("upload", uploadId)), Cancel);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await Json(created);
        project["name"]!.GetValue<string>().Should().Be("cat-keyboard");
        project["revision"]!.GetValue<int>().Should().Be(1);
        project["source"]!["fileName"]!.GetValue<string>().Should().Be("cat-keyboard.gif");
        project["source"]!.AsObject().Should().NotContainKey("path", "a browser has no use for the server's paths");
        var segment = project["scene"]!["segments"]!.AsArray().Single()!;
        segment["name"]!.GetValue<string>().Should().Be("Стикер 1");
        segment["outFrame"]!.GetValue<int>().Should().Be(29);
        segment["frame"]!.ToJsonString().Should().Be(
            """{"mode":"crop","preset":"square","rect":{"x":105,"y":0,"width":270,"height":270}}""");
        ProjectFiles().Select(Path.GetFileName).Should().Equal("cat-keyboard.ssproj");

        var id = project["id"]!.GetValue<string>();
        var read = await client.GetAsync($"/api/v1/projects/{id}", Cancel);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(read))["source"]!["status"]!.GetValue<string>().Should().Be("ok");
        var list = await Json(await client.GetAsync("/api/v1/projects", Cancel));
        list.AsArray().Select(card => card!["id"]!.GetValue<string>()).Should().Equal(id);
    }

    [Fact]
    public async Task Makes_a_project_of_a_path_in_desktop_mode_and_numbers_a_second_one()
    {
        var client = await ClientAsync(_desktop);
        var video = Path.Combine(_files.FullName, "cat.gif");
        await File.WriteAllBytesAsync(video, Gif, Cancel);

        var first = await client.PostAsync("/api/v1/projects", Body(SourceBody("path", video)), Cancel);
        var second = await client.PostAsync("/api/v1/projects", Body(SourceBody("path", video)), Cancel);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(first))["source"]!["path"]!.GetValue<string>().Should().Be(video);
        (await Json(second))["path"]!.GetValue<string>().Should().Be(Path.Combine(ProjectsFolder, "cat (2).ssproj"));
    }

    [Fact]
    public async Task Answers_a_broken_video_with_the_engine_s_words()
    {
        _probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<MediaInfo>(_ =>
            throw new AppException(ErrorCode.SourceUnreadable, "Cannot read") { Log = "moov atom not found" });
        var client = await ClientAsync(_web);
        var uploadId = (await Json(await UploadAsync(client, Gif, "cat.gif")))["uploadId"]!.GetValue<string>();

        var response = await client.PostAsync("/api/v1/projects", Body(SourceBody("upload", uploadId)), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("source_unreadable");
        problem["log"]!.GetValue<string>().Should().Be("moov atom not found");
        ProjectFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_an_avi_with_its_extension()
    {
        var client = await ClientAsync(_web);

        var response = await UploadAsync(client, Encoding.ASCII.GetBytes("RIFF\0\0\0\0AVI LIST"), "clip.avi");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("format_unsupported");
        problem["params"]!["ext"]!.GetValue<string>().Should().Be("avi");
        var uploads = Path.Combine(_web.DataDir, "uploads");
        (Directory.Exists(uploads) ? Directory.GetFiles(uploads, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_body_without_a_file()
    {
        var client = await ClientAsync(_web);
        var form = new MultipartFormDataContent { { new StringContent("cat"), "name" } };

        var response = await client.PostAsync("/api/v1/uploads", form, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["fields"]!["file"]!.AsArray().Single()!.GetValue<string>().Should().Be("required");
    }

    [Fact]
    public async Task Refuses_a_path_from_a_browser()
    {
        var client = await ClientAsync(_web);

        var response = await client.PostAsync("/api/v1/projects", Body(SourceBody("path", "/etc/passwd")), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _probe.DidNotReceiveWithAnyArgs().ProbeAsync(default!, Cancel);
    }

    [Fact]
    public async Task Finds_no_upload_used_once_already()
    {
        var client = await ClientAsync(_web);
        var uploadId = (await Json(await UploadAsync(client, Gif, "cat.gif")))["uploadId"]!.GetValue<string>();
        (await client.PostAsync("/api/v1/projects", Body(SourceBody("upload", uploadId)), Cancel)).EnsureSuccessStatusCode();

        var again = await client.PostAsync("/api/v1/projects", Body(SourceBody("upload", uploadId)), Cancel);

        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(again))["code"]!.GetValue<string>().Should().Be("source_not_found");
    }

    [Fact]
    public async Task Creates_nothing_when_the_request_is_dropped_while_the_video_is_read()
    {
        var reading = new TaskCompletionSource();
        var stopped = new TaskCompletionSource();
        _probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var token = call.Arg<CancellationToken>();
            await using var _ = token.Register(() => stopped.TrySetResult());
            reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Scenes.Source;
        });
        var client = await ClientAsync(_web);
        var uploadId = (await Json(await UploadAsync(client, Gif, "cat.gif")))["uploadId"]!.GetValue<string>();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel);

        var create = client.PostAsync("/api/v1/projects", Body(SourceBody("upload", uploadId)), cancel.Token);
        await reading.Task.WaitAsync(Cancel);
        await cancel.CancelAsync();

        await create.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        await stopped.Task.WaitAsync(Cancel);
        ProjectFiles().Should().BeEmpty();
        (await Json(await client.GetAsync("/api/v1/projects", Cancel))).AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Replaces_a_missing_source_and_warns_about_the_segments_past_its_end()
    {
        var client = await ClientAsync(_desktop);
        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        project["source"]!["path"] = Path.Combine(_files.FullName, "gone.gif");
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        (await client.PostAsync("/api/v1/projects/open", Body(new JsonObject { ["path"] = path }), Cancel)).EnsureSuccessStatusCode();
        var video = Path.Combine(_files.FullName, "shorter.gif");
        await File.WriteAllBytesAsync(video, Gif, Cancel);
        _probe.ProbeAsync(video, Arg.Any<CancellationToken>()).Returns(Scenes.Source with { FrameCount = 25 });

        var response = await client.PutAsync(
            $"/api/v1/projects/{SampleProjects.Sample.Id}/source", Body(SourceBody("path", video)), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var replaced = await Json(response);
        replaced["segmentsOutOfRange"]!.ToJsonString().Should().Be("""["seg-1"]""");
        replaced["project"]!["source"]!["path"]!.GetValue<string>().Should().Be(video);
        replaced["project"]!["source"]!["status"]!.GetValue<string>().Should().Be("ok");
        replaced["project"]!["source"]!["frameCount"]!.GetValue<int>().Should().Be(25);
        replaced["project"]!["revision"]!.GetValue<int>().Should().Be(SampleProjects.Sample.Revision);
    }
}
