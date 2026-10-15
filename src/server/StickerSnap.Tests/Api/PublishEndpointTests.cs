using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Api;

/// <summary>GET /api/v1/projects/{id}/publish-draft over the sample project, with no account and no packs.</summary>
public sealed class PublishEndpointTests : IAsyncLifetime
{
    private const string CatId = "6f1c2a4e-8b3d-4f7a-9c21-5d0e7a3b9f12";

    private readonly DirectoryInfo _files = Directory.CreateTempSubdirectory("stickersnap-publish-");
    private readonly ApiFactory _factory = new(("Mode", "desktop"));
    private HttpClient _client = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        var project = JsonNode.Parse(SampleProjects.Text(SampleProjects.SamplePath))!;
        project["source"]!["path"] = Path.Combine(_files.FullName, "cat-keyboard.gif");
        var path = Path.Combine(_files.FullName, "cat.ssproj");
        await File.WriteAllTextAsync(path, project.ToJsonString(), Cancel);
        var body = new StringContent(new JsonObject { ["path"] = path }.ToJsonString(), Encoding.UTF8, "application/json");
        (await _client.PostAsync("/api/v1/projects/open", body, Cancel)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        _files.Delete(recursive: true);
    }

    [Fact]
    public async Task Gives_the_segments_the_account_and_no_packs_yet()
    {
        var draft = JsonNode.Parse(await _client.GetStringAsync($"/api/v1/projects/{CatId}/publish-draft", Cancel))!;

        draft["account"]!["state"]!.GetValue<string>().Should().Be("unlinked");
        draft["segments"]!.AsArray().Select(segment => segment!["segmentId"]!.GetValue<string>()).Should().Equal("seg-1", "seg-2");
        draft["segments"]![1]!["preset"]!.GetValue<string>().Should().Be("emoji");
        draft["segments"]![0]!["publishedIn"]!.AsArray().Should().BeEmpty();
        draft["packs"]!.AsArray().Should().BeEmpty();
        draft["recentEmoji"]!.AsArray().Should().BeEmpty();
        draft.AsObject().ContainsKey("lastPack").Should().BeFalse();
    }

    [Fact]
    public async Task Answers_project_not_found_for_a_project_off_the_list()
    {
        var response = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/publish-draft", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!["code"]!.GetValue<string>().Should().Be("project_not_found");
    }
}
