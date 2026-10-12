using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Api.Events;

/// <summary>A SignalR client of /hubs/events, as the SPA connects, against the server in memory.</summary>
public sealed class EventsHubTests
{
    private const string Token = "launch-token";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static HubConnection Connect(ApiFactory factory, string? token = null)
    {
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/events"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    [Fact]
    public async Task Sends_settings_changed_with_the_whole_settings_after_a_patch()
    {
        using var factory = new ApiFactory();
        await using var connection = Connect(factory);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("settingsChanged", payload => received.TrySetResult(payload));
        await connection.StartAsync(Cancel);

        var response = await factory.CreateClient().PatchAsync("/api/v1/settings",
            new StringContent("""{"theme":"light","accent":"teal"}""", Encoding.UTF8, "application/json"), Cancel);
        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetRawText().Should().Be(await response.Content.ReadAsStringAsync(Cancel));
        payload.GetProperty("theme").GetString().Should().Be("light");
        payload.GetProperty("accent").GetString().Should().Be("teal");
    }

    [Fact]
    public async Task Sends_project_changed_with_the_card_after_a_save_and_project_removed_after_forgetting()
    {
        using var factory = new ApiFactory();
        var folder = Directory.CreateTempSubdirectory("stickersnap-hub-");
        try
        {
            var path = await TestFiles.CopyToAsync("Projects/cat-keyboard.ssproj", folder.FullName, Cancel);
            var client = factory.CreateClient();
            var opened = await client.PostAsync("/api/v1/projects/open",
                new StringContent(JsonSerializer.Serialize(new { path }), Encoding.UTF8, "application/json"), Cancel);
            var project = JsonDocument.Parse(await opened.Content.ReadAsStringAsync(Cancel)).RootElement;
            var id = project.GetProperty("id").GetString();

            await using var connection = Connect(factory);
            var changed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            var removed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.On<JsonElement>("projectChanged", payload => changed.TrySetResult(payload));
            connection.On<JsonElement>("projectRemoved", payload => removed.TrySetResult(payload));
            await connection.StartAsync(Cancel);

            var save = new { revision = 12, scene = project.GetProperty("scene") };
            await client.PutAsync($"/api/v1/projects/{id}/scene",
                new StringContent(JsonSerializer.Serialize(save), Encoding.UTF8, "application/json"), Cancel);
            var card = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
            await client.DeleteAsync($"/api/v1/projects/{id}?mode=forget", Cancel);
            var gone = await removed.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

            card.GetProperty("id").GetString().Should().Be(id);
            card.GetProperty("segmentCount").GetInt32().Should().Be(2);
            card.GetProperty("sourceStatus").GetString().Should().Be("missing");
            gone.GetRawText().Should().Be($$"""{"id":"{{id}}"}""");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Lets_in_a_client_with_the_launch_token()
    {
        using var factory = new ApiFactory(("Token", Token));
        await using var connection = Connect(factory, Token);

        await connection.StartAsync(Cancel);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Keeps_out_a_client_without_the_launch_token()
    {
        using var factory = new ApiFactory(("Token", Token));
        await using var connection = Connect(factory);

        var start = () => connection.StartAsync(Cancel);

        (await start.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
