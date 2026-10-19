using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StickerSnap.Application.Account;
using StickerSnap.Core.Account;
using StickerSnap.Tests.Application.Account;

namespace StickerSnap.Tests.Api;

/// <summary>/api/v1/account and accountChanged, with the linking service faked and account.json in a temporary data directory.</summary>
public sealed class AccountEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly FakeDeviceLinking _linking = new(TimeProvider.System);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private WebApplicationFactory<StickerSnap.Api.Program> WithLinking() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IDeviceLinking>(_linking)));

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Cancel))!;

    [Fact]
    public async Task Is_unlinked_on_a_first_launch()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/account", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancel)).Should().Be("""{"state":"unlinked"}""");
    }

    [Fact]
    public async Task Gives_no_link_when_the_server_has_no_service()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/account/link", null, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await Json(response))["code"]!.GetValue<string>().Should().Be("bot_unavailable");
    }

    [Fact]
    public async Task Links_the_computer_when_the_user_confirms_and_says_so_over_the_hub()
    {
        using var server = WithLinking();
        var client = server.CreateClient();
        await using var hub = Connect(server.Server);
        var linked = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("accountChanged", payload =>
        {
            if (payload.GetProperty("state").GetString() == "linked") linked.TrySetResult(payload);
        });
        await hub.StartAsync(Cancel);

        var pending = await Json(await client.PostAsync("/api/v1/account/link", null, Cancel));
        await _linking.ParkedAsync();
        _linking.Answer(new LinkAnswer.Linked(new LinkedDevice("dev_1", "DESKTOP-7F3K2Q", DateTimeOffset.UtcNow, new LinkedUser(42, "Дима", "dima"), false)));
        var payload = await linked.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        pending["state"]!.GetValue<string>().Should().Be("pending");
        pending["deepLink"]!.GetValue<string>().Should().Be("https://t.me/StickerSnapAppBot?startapp=link_code1");
        pending["expiresAt"].Should().NotBeNull();
        payload.GetRawText().Should().Be("""{"state":"linked","user":{"id":42,"username":"dima","firstName":"Дима"},"device":"DESKTOP-7F3K2Q"}""");
        (await client.GetStringAsync("/api/v1/account", Cancel)).Should().Be(payload.GetRawText());
        File.Exists(Path.Combine(_factory.DataDir, "account.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Says_why_the_last_link_ended()
    {
        using var server = WithLinking();
        var client = server.CreateClient();
        await using var hub = Connect(server.Server);
        var ended = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("accountChanged", payload =>
        {
            if (payload.GetProperty("state").GetString() == "unlinked") ended.TrySetResult(payload);
        });
        await hub.StartAsync(Cancel);
        await client.PostAsync("/api/v1/account/link", null, Cancel);
        await _linking.ParkedAsync();

        _linking.Answer(new LinkAnswer.Declined());
        var payload = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        payload.GetRawText().Should().Be("""{"state":"unlinked","reason":"declined"}""");
        (await client.GetStringAsync("/api/v1/account", Cancel)).Should().Be(payload.GetRawText());
    }

    [Fact]
    public async Task Cancels_the_wait_and_unlinks()
    {
        using var server = WithLinking();
        var client = server.CreateClient();
        await client.PostAsync("/api/v1/account/link", null, Cancel);

        var cancelled = await client.DeleteAsync("/api/v1/account/link", Cancel);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(cancelled))["state"]!.GetValue<string>().Should().Be("unlinked");
    }

    [Fact]
    public async Task Check_says_the_bot_is_blocked()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_factory.DataDir, "account.json"),
            """{ "version": 2, "user": { "id": 42, "firstName": "Дима" }, "device": "DESKTOP-7F3K2Q", "botBlocked": false }""",
            Cancel);
        _linking.Device = new LinkedDevice("dev_1", "DESKTOP-7F3K2Q", DateTimeOffset.UtcNow, new LinkedUser(42, "Дима", null), BotBlocked: true);
        using var server = WithLinking();

        var response = await server.CreateClient().PostAsync("/api/v1/account/check", null, Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancel))
            .Should().Be("""{"state":"error","code":"bot_blocked","user":{"id":42,"firstName":"Дима"},"device":"DESKTOP-7F3K2Q"}""");
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
