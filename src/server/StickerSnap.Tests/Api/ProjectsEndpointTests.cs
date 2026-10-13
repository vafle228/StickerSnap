using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using StickerSnap.Tests.Infrastructure.Projects;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Api;

/// <summary>
/// /api/v1/projects over real .ssproj files and projects.json in temporary directories: what the
/// SPA sends and gets, and what lands on the disk.
/// </summary>
public sealed class ProjectsEndpointTests : IDisposable
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-projects-");
    private readonly ApiFactory _factory = new(("Mode", "desktop"));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _factory.Dispose();
        _files.Delete(recursive: true);
    }

    /// <summary>The sample project in the temporary folder, its source beside it.</summary>
    private async Task<string> SampleAsync(string name = "cat.ssproj", bool withSource = true)
    {
        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        var source = Path.Combine(_files.FullName, "cat-keyboard.gif");
        project["source"]!["path"] = source;
        if (withSource) await File.WriteAllBytesAsync(source, [0x47, 0x49, 0x46], Cancel);

        var path = Path.Combine(_files.FullName, name);
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        return path;
    }

    private async Task<string> TestFileAsync(string sample) =>
        await TestFiles.CopyToAsync(sample, _files.FullName, Cancel);

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private Task<HttpResponseMessage> OpenAsync(ApiFactory factory, string path) =>
        factory.CreateClient().PostAsync("/api/v1/projects/open", Body(new JsonObject { ["path"] = path }), Cancel);

    private Task<HttpResponseMessage> SaveAsync(int revision, JsonNode scene) =>
        _factory.CreateClient().PutAsync(
            $"/api/v1/projects/{CatId}/scene", Body(new JsonObject { ["revision"] = revision, ["scene"] = scene.DeepClone() }), Cancel);

    private static string CodeOf(JsonNode problem) => problem["code"]!.GetValue<string>();

    [Fact]
    public async Task Opens_a_file_with_its_scene_source_and_addresses()
    {
        var path = await SampleAsync();

        var response = await OpenAsync(_factory, path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var project = await Json(response);
        project["id"]!.GetValue<string>().Should().Be(CatId);
        project["name"]!.GetValue<string>().Should().Be("Кот и клавиатура");
        project["path"]!.GetValue<string>().Should().Be(path);
        project["revision"]!.GetValue<int>().Should().Be(12);
        // v follows the source's content, so a new video is loaded again by the <video>
        project["mediaUrl"]!.GetValue<string>().Should().MatchRegex($@"^/api/v1/projects/{CatId}/media\?v=[0-9a-f]{{12}}$");
        project["exports"]!.AsObject().Should().BeEmpty();
        var source = project["source"]!;
        source["status"]!.GetValue<string>().Should().Be("ok");
        source["path"]!.GetValue<string>().Should().EndWith("cat-keyboard.gif");
        source["fps"]!.ToJsonString().Should().Be("""{"num":10,"den":1}""");
        source.AsObject().Should().NotContainKey("fingerprint");
        var segments = project["scene"]!["segments"]!.AsArray();
        segments.Should().HaveCount(2);
        segments[0]!["frame"]!["mode"]!.GetValue<string>().Should().Be("crop");
        segments[1]!["loop"]!.GetValue<string>().Should().Be("pingpong");
    }

    [Fact]
    public async Task Lists_an_opened_project_as_a_card()
    {
        await OpenAsync(_factory, await SampleAsync(withSource: false));

        var list = await _factory.CreateClient().GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel);

        var card = list!.Should().ContainSingle().Which!;
        card["id"]!.GetValue<string>().Should().Be(CatId);
        card["segmentCount"]!.GetValue<int>().Should().Be(2);
        card["preset"]!.GetValue<string>().Should().Be("mixed");
        card["durationSec"]!.GetValue<double>().Should().Be(3);
        // v follows the last save, so the card's <img> loads the new cover after an edit
        card["coverUrl"]!.GetValue<string>().Should().Be($"/api/v1/projects/{CatId}/cover?v=1791797400000");
        card["sourceStatus"]!.GetValue<string>().Should().Be("missing");
        card["updatedAt"]!.GetValue<DateTimeOffset>().Should().Be(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Hides_the_source_path_in_web_mode()
    {
        using var web = new ApiFactory();

        var project = await Json(await OpenAsync(web, await SampleAsync()));

        project["source"]!.AsObject().Should().NotContainKey("path");
    }

    [Fact]
    public async Task Saves_the_scene_into_the_file_and_raises_the_revision()
    {
        var path = await SampleAsync();
        var scene = (await Json(await OpenAsync(_factory, path)))["scene"]!;
        scene["segments"]![0]!["outFrame"] = 9;

        var response = await SaveAsync(12, scene);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await Json(response);
        saved["revision"]!.GetValue<int>().Should().Be(13);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(path, Cancel))!;
        file["revision"]!.GetValue<int>().Should().Be(13);
        file["scene"]!["segments"]![0]!["outFrame"]!.GetValue<int>().Should().Be(9);
        var again = await _factory.CreateClient().GetFromJsonAsync<JsonNode>($"/api/v1/projects/{CatId}", Cancel);
        again!["scene"]!.ToJsonString().Should().Be(scene.ToJsonString());
        again["savedAt"]!.GetValue<DateTimeOffset>().Should().Be(saved["savedAt"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task Refuses_a_save_over_an_old_revision_with_409()
    {
        var path = await SampleAsync();
        var scene = (await Json(await OpenAsync(_factory, path)))["scene"]!;
        (await SaveAsync(12, scene)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SaveAsync(12, scene);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        CodeOf(await Json(response)).Should().Be("revision_conflict");
    }

    [Fact]
    public async Task Refuses_a_scene_past_the_end_of_its_source_with_the_field()
    {
        var scene = (await Json(await OpenAsync(_factory, await SampleAsync())))["scene"]!;
        scene["segments"]![0]!["outFrame"] = 31;

        var response = await SaveAsync(12, scene);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        CodeOf(problem).Should().Be("bad_request");
        problem["fields"]!["scene.segments[0].outFrame"]!.AsArray().Select(rule => rule!.GetValue<string>())
            .Should().Equal("out_of_range");
    }

    [Fact]
    public async Task Refuses_a_frame_without_its_mode()
    {
        var scene = (await Json(await OpenAsync(_factory, await SampleAsync())))["scene"]!;
        scene["segments"]![0]!["frame"]!.AsObject().Remove("mode");

        var response = await SaveAsync(12, scene);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        CodeOf(await Json(response)).Should().Be("bad_request");
    }

    [Fact]
    public async Task Renames_inside_the_file_and_leaves_the_file_name()
    {
        var path = await SampleAsync();
        await OpenAsync(_factory, path);

        var response = await _factory.CreateClient().PatchAsync(
            $"/api/v1/projects/{CatId}", Body(new JsonObject { ["name"] = "Кот на диване" }), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["name"]!.GetValue<string>().Should().Be("Кот на диване");
        var file = JsonNode.Parse(await File.ReadAllTextAsync(path, Cancel))!;
        file["name"]!.GetValue<string>().Should().Be("Кот на диване");
        file["revision"]!.GetValue<int>().Should().Be(12);
    }

    [Fact]
    public async Task Forgets_a_project_leaves_its_file_and_brings_it_back_by_its_path()
    {
        var path = await SampleAsync();
        await OpenAsync(_factory, path);
        var client = _factory.CreateClient();

        var forgotten = await client.DeleteAsync($"/api/v1/projects/{CatId}?mode=forget", Cancel);
        var list = await client.GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel);

        forgotten.StatusCode.Should().Be(HttpStatusCode.NoContent);
        list.Should().BeEmpty();
        File.Exists(path).Should().BeTrue();
        await _factory.Trash.DidNotReceiveWithAnyArgs().MoveToTrashAsync(default!, Cancel);
        (await client.GetAsync($"/api/v1/projects/{CatId}", Cancel)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await OpenAsync(_factory, path)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel)).Should().ContainSingle();
    }

    [Fact]
    public async Task Sends_the_file_to_the_trash()
    {
        var path = await SampleAsync();
        await OpenAsync(_factory, path);

        var response = await _factory.CreateClient().DeleteAsync($"/api/v1/projects/{CatId}?mode=trash", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _factory.Trash.Received(1).MoveToTrashAsync(path, Arg.Any<CancellationToken>());
        (await _factory.CreateClient().GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("?mode=burn")]
    public async Task Refuses_a_removal_without_a_known_mode(string query)
    {
        await OpenAsync(_factory, await SampleAsync());

        var response = await _factory.CreateClient().DeleteAsync($"/api/v1/projects/{CatId}{query}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        CodeOf(await Json(response)).Should().Be("bad_request");
    }

    [Fact]
    public async Task Answers_project_not_found_for_a_project_off_the_list()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/v1/projects/{Guid.NewGuid()}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        CodeOf(await Json(response)).Should().Be("project_not_found");
    }

    [Fact]
    public async Task Answers_project_not_found_when_its_file_is_gone()
    {
        var path = await SampleAsync();
        await OpenAsync(_factory, path);
        File.Delete(path);

        var response = await _factory.CreateClient().GetAsync($"/api/v1/projects/{CatId}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        CodeOf(await Json(response)).Should().Be("project_not_found");
    }

    [Theory]
    [InlineData("Projects/too-new.ssproj", "project_too_new")]
    [InlineData("Projects/wrong-type.ssproj", "project_invalid")]
    [InlineData("Projects/truncated.ssproj", "project_invalid")]
    public async Task Refuses_to_open_a_file_it_cannot_read_with_422(string sample, string code)
    {
        var response = await OpenAsync(_factory, await TestFileAsync(sample));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await Json(response);
        CodeOf(problem).Should().Be(code);
        problem["log"]!.GetValue<string>().Should().NotBeEmpty();
        (await _factory.CreateClient().GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel)).Should().BeEmpty();
    }

    [Fact]
    public async Task Answers_422_for_a_listed_project_whose_file_broke()
    {
        var path = await SampleAsync();
        await OpenAsync(_factory, path);
        await File.WriteAllTextAsync(path, "{", Cancel);

        var response = await _factory.CreateClient().GetAsync($"/api/v1/projects/{CatId}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        CodeOf(await Json(response)).Should().Be("project_invalid");
    }

    [Fact]
    public async Task Refuses_to_open_a_relative_path()
    {
        var response = await OpenAsync(_factory, "cat.ssproj");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["fields"]!["path"]![0]!.GetValue<string>().Should().Be("not_absolute");
    }

    [Fact]
    public async Task Keeps_the_list_across_a_restart_of_the_server()
    {
        var dataDir = Directory.CreateTempSubdirectory("stickersnap-restart-").FullName;
        try
        {
            using (var before = new ApiFactory { DataDir = dataDir })
                (await OpenAsync(before, await SampleAsync())).StatusCode.Should().Be(HttpStatusCode.OK);

            using var after = new ApiFactory { DataDir = dataDir };
            var list = await after.CreateClient().GetFromJsonAsync<JsonArray>("/api/v1/projects", Cancel);

            list.Should().ContainSingle().Which!["id"]!.GetValue<string>().Should().Be(CatId);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }
}
