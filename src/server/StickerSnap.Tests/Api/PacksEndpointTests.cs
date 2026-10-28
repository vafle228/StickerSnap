using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Tests.Infrastructure.Cloud;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Api;

/// <summary>
/// /api/v1/packs and packChanged against a faked cloud service (its pack in TestData/Cloud), on a
/// linked computer, with packs.json and the stickers' files in a temporary data directory. No bot
/// token anywhere: the server reaches Telegram only through the service.
/// </summary>
public sealed class PacksEndpointTests : IDisposable, IAsyncLifetime
{
    private const string Cats = "cats_by_StickerSnapAppBot";

    private readonly CloudServiceStub _cloud = new();
    private readonly ApiFactory _factory;
    private readonly WebApplicationFactory<StickerSnap.Api.Program> _server;

    public PacksEndpointTests()
    {
        _factory = new ApiFactory(("ServiceUrl", CloudServiceStub.ServiceUrl.ToString())) { DataDir = _cloud.DataDir };
        _server = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<CloudClient>().ConfigurePrimaryHttpMessageHandler(() => _cloud)));
        _cloud.Answer($"GET /v1/packs/{Cats}", TestFiles.ReadText("Cloud/cats.json"));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _cloud.LinkAsync(Cancel);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        _server.Dispose();
        _factory.Dispose();
        _cloud.Dispose();
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    private Task<HttpResponseMessage> ImportAsync(HttpClient client, string link) =>
        client.PostAsJsonAsync("/api/v1/packs/import", new { link }, Cancel);

    [Fact]
    public async Task Has_no_packs_on_a_first_launch()
    {
        (await _server.CreateClient().GetStringAsync("/api/v1/packs", Cancel)).Should().Be("[]");
    }

    [Fact]
    public async Task Adds_a_pack_of_the_bot_by_its_link_and_lists_it()
    {
        var client = _server.CreateClient();

        var response = await ImportAsync(client, $"https://t.me/addstickers/{Cats}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pack = await Json(response);
        pack["name"]!.GetValue<string>().Should().Be(Cats);
        pack["title"]!.GetValue<string>().Should().Be("Коты");
        pack["type"]!.GetValue<string>().Should().Be("regular");
        pack["count"]!.GetValue<int>().Should().Be(2);
        pack["limit"]!.GetValue<int>().Should().Be(120);
        pack["status"]!.GetValue<string>().Should().Be("ok");
        pack["checkedAt"].Should().NotBeNull();
        pack["link"]!.GetValue<string>().Should().Be($"https://t.me/addstickers/{Cats}");
        pack["coverUrl"]!.GetValue<string>().Should().Be($"/api/v1/packs/{Cats}/stickers/AgADc2/media");
        JsonNode.DeepEquals(pack["stickers"], JsonNode.Parse(
            $$"""[{"id":"AgADc2","emoji":["😂"],"mediaUrl":"/api/v1/packs/{{Cats}}/stickers/AgADc2/media","format":"video"},{"id":"AgADc3","emoji":[],"mediaUrl":"/api/v1/packs/{{Cats}}/stickers/AgADc3/media","format":"video"}]""")).Should().BeTrue(pack["stickers"]!.ToJsonString());

        var list = await Json(await client.GetAsync("/api/v1/packs", Cancel));
        list.AsArray().Should().ContainSingle();
        list[0]!["stickers"].Should().BeNull("a row of the list has no stickers");
        File.Exists(Path.Combine(_factory.DataDir, "packs.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_a_pack_of_another_bot_with_422()
    {
        var response = await ImportAsync(_server.CreateClient(), "https://t.me/addstickers/Animals_by_OtherBot");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response))["code"]!.GetValue<string>().Should().Be("not_our_pack");
        _cloud.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Passes_telegrams_wait_on_as_rate_limited_with_its_seconds()
    {
        _cloud.Refuse($"GET /v1/packs/{Cats}", 429, "rate_limited", """{"retryAfterSec":24}""", retryAfter: 24);

        var response = await ImportAsync(_server.CreateClient(), $"https://t.me/addstickers/{Cats}");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = await Json(response);
        problem["code"]!.GetValue<string>().Should().Be("rate_limited");
        problem["params"]!["retryAfterSec"]!.GetValue<int>().Should().Be(24);
    }

    [Fact]
    public async Task Says_a_pack_off_the_list_is_not_found()
    {
        var response = await _server.CreateClient().GetAsync($"/api/v1/packs/{Cats}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("pack_not_found");
    }

    [Fact]
    public async Task Serves_a_stickers_file_again_without_the_network()
    {
        var client = _server.CreateClient();
        (await ImportAsync(client, $"https://t.me/addstickers/{Cats}")).EnsureSuccessStatusCode();
        var file = "GET /v1/stickers/CAACAgIAAxkBAAIBc2/file";
        _cloud.Answer(file, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("webm bytes") });
        var media = $"/api/v1/packs/{Cats}/stickers/AgADc2/media";

        var first = await client.GetAsync(media, Cancel);
        _cloud.Throw(file, new HttpRequestException("No route to host"));
        var second = await client.GetAsync(media, Cancel);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("video/webm");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync(Cancel)).Should().Be("webm bytes");
        _cloud.Requests.Count(request => request.Route == file).Should().Be(1);
    }

    [Fact]
    public async Task Deletes_a_sticker_in_telegram_and_says_so_over_the_hub()
    {
        var client = _server.CreateClient();
        (await ImportAsync(client, $"https://t.me/addstickers/{Cats}")).EnsureSuccessStatusCode();
        await using var hub = Connect(_server.Server);
        var changed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("packChanged", payload => changed.TrySetResult(payload));
        await hub.StartAsync(Cancel);

        var response = await client.DeleteAsync($"/api/v1/packs/{Cats}/stickers/AgADc3", Cancel);
        var payload = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _cloud.Single($"DELETE /v1/packs/{Cats}/stickers/CAACAgIAAxkBAAIBc3").Authorization.Should().Be($"Bearer {CloudServiceStub.Key}");
        payload.GetProperty("name").GetString().Should().Be(Cats);
        payload.GetProperty("count").GetInt32().Should().Be(1);
        var pack = await Json(await client.GetAsync($"/api/v1/packs/{Cats}", Cancel));
        pack["stickers"]!.AsArray().Select(sticker => sticker!["id"]!.GetValue<string>()).Should().Equal("AgADc2");
    }

    [Fact]
    public async Task Checks_a_name_with_the_service_and_offers_a_variant()
    {
        const string taken = "kot_i_divan_by_StickerSnapAppBot";
        _cloud.Answer($"GET /v1/pack-names/{taken}", $$"""{"name":"{{taken}}","taken":true}""");
        _cloud.Answer("GET /v1/pack-names/kot_i_divan_2_by_StickerSnapAppBot", """{"name":"kot_i_divan_2_by_StickerSnapAppBot","taken":false}""");

        var response = await _server.CreateClient().PostAsJsonAsync("/api/v1/packs/name-check", new { title = "Кот и диван" }, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be(
            """{"name":"kot_i_divan","fullName":"kot_i_divan_by_StickerSnapAppBot","problems":["taken"],"suggestion":"kot_i_divan_2"}""");
    }

    [Fact]
    public async Task Copies_a_pack_of_another_bot_and_serves_its_static_stickers_as_webp()
    {
        const string copy = "starye_koty_by_StickerSnapAppBot";
        _cloud.Answer($"GET /v1/pack-names/{copy}", $$"""{"name":"{{copy}}","taken":false}""");
        _cloud.Answer("POST /v1/packs/copies", $$"""
            {"name":"{{copy}}","title":"Старые коты","kind":"stickers","stickers":[
            {"id":"F1","uniqueId":"U1","emoji":["😂"],"format":"static"}]}
            """, HttpStatusCode.Created);
        _cloud.Answer("GET /v1/stickers/F1/file", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) });
        var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/packs/copy", new { link = "https://t.me/addstickers/OldCats", title = "Старые коты" }, Cancel);
        var media = await client.GetAsync($"/api/v1/packs/{copy}/stickers/U1/media", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pack = await Json(response);
        pack["name"]!.GetValue<string>().Should().Be(copy);
        pack["stickers"]![0]!["format"]!.GetValue<string>().Should().Be("static");
        (await client.GetStringAsync("/api/v1/packs", Cancel)).Should().Contain(copy);
        media.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Says_a_pack_the_service_does_not_give_is_not_found()
    {
        _cloud.Refuse($"GET /v1/packs/{Cats}", 404, "pack_not_found");

        var response = await ImportAsync(_server.CreateClient(), $"https://t.me/addstickers/{Cats}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("pack_not_found");
    }

    [Fact]
    public async Task Says_an_unlinked_computer_cannot_reach_its_packs()
    {
        await _cloud.Device.ForgetKeyAsync(Cancel);

        var response = await ImportAsync(_server.CreateClient(), $"https://t.me/addstickers/{Cats}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["code"]!.GetValue<string>().Should().Be("account_not_linked");
        _cloud.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Forgets_a_pack()
    {
        var client = _server.CreateClient();
        (await ImportAsync(client, $"https://t.me/addstickers/{Cats}")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/v1/packs/{Cats}", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetStringAsync("/api/v1/packs", Cancel)).Should().Be("[]");
    }

    private static HubConnection Connect(TestServer server) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/events"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
}
