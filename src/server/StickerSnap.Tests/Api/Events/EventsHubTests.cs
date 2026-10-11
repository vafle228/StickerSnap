using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

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
