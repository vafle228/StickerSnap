using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace StickerSnap.Tests.Api;

/// <summary>GET and PATCH /api/v1/settings over the real repository in a temporary data directory.</summary>
public sealed class SettingsEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private static Task<HttpResponseMessage> Patch(ApiFactory factory, string body) =>
        factory.CreateClient().PatchAsync("/api/v1/settings", new StringContent(body, Encoding.UTF8, "application/json"), Cancel);

    [Fact]
    public async Task Gives_the_defaults_on_a_first_launch()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/settings", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await Json(response);
        settings["theme"]!.GetValue<string>().Should().Be("system");
        settings["accent"]!.GetValue<string>().Should().Be("indigo");
        settings["recentEmoji"]!.AsArray().Should().BeEmpty();
        var projects = settings["folders"]!["projects"]!.GetValue<string>();
        Path.IsPathFullyQualified(projects).Should().BeTrue();
        projects.Should().EndWith(Path.Combine("StickerSnap", "Проекты"));
        settings["folders"]!["stickers"]!.GetValue<string>().Should().EndWith(Path.Combine("StickerSnap", "Стикеры"));
    }

    [Fact]
    public async Task Changes_the_given_fields_and_answers_with_the_whole_settings()
    {
        var stickers = Path.Combine(_factory.DataDir, "Out");

        var response = await Patch(_factory, new JsonObject
        {
            ["theme"] = "dark",
            ["folders"] = new JsonObject { ["stickers"] = stickers },
        }.ToJsonString());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await Json(response);
        settings["theme"]!.GetValue<string>().Should().Be("dark");
        settings["accent"]!.GetValue<string>().Should().Be("indigo");
        settings["folders"]!["stickers"]!.GetValue<string>().Should().Be(stickers);
        Directory.Exists(stickers).Should().BeTrue();
    }

    [Fact]
    public async Task Keeps_a_change_across_a_restart_of_the_server()
    {
        var dataDir = Directory.CreateTempSubdirectory("stickersnap-restart-").FullName;
        try
        {
            using (var before = new ApiFactory { DataDir = dataDir })
                (await Patch(before, """{"accent":"raspberry"}""")).StatusCode.Should().Be(HttpStatusCode.OK);

            using var after = new ApiFactory { DataDir = dataDir };
            var settings = await after.CreateClient().GetFromJsonAsync<JsonNode>("/api/v1/settings", Cancel);

            settings!["accent"]!.GetValue<string>().Should().Be("raspberry");
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"folders":{}}""")]
    [InlineData("""{"theme":"neon"}""")]
    [InlineData("""{"recentEmoji":["😂"]}""")]
    [InlineData("""{"theme":"dark","color":"red"}""")]
    [InlineData("not json")]
    public async Task Answers_bad_request_to_a_patch_outside_the_schema(string body)
    {
        var response = await Patch(_factory, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await Json(response))["code"]!.GetValue<string>().Should().Be("bad_request");
    }

    [Fact]
    public async Task Names_the_field_of_a_relative_folder()
    {
        var response = await Patch(_factory, """{"folders":{"projects":"Projects"}}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("bad_request");
        problem["fields"]!.ToJsonString().Should().Be("""{"folders.projects":["not_absolute"]}""");
    }

    [Fact]
    public async Task Answers_save_failed_for_a_folder_it_cannot_create()
    {
        var file = Path.Combine(_factory.DataDir, "a-file");
        await File.WriteAllTextAsync(file, "", Cancel);

        var response = await Patch(_factory, new JsonObject
        {
            ["folders"] = new JsonObject { ["projects"] = Path.Combine(file, "Projects") },
        }.ToJsonString());

        response.StatusCode.Should().Be(HttpStatusCode.InsufficientStorage);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("save_failed");
    }
}
