using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace StickerSnap.Tests.Api;

/// <summary>A client of /hubs/events that keeps every jobUpdated and can await one.</summary>
internal sealed class JobUpdates(HubConnection hub) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly List<JsonElement> _received = [];
    private readonly List<(string State, TaskCompletionSource<JsonElement> Found)> _awaited = [];

    public IReadOnlyList<JsonElement> Received
    {
        get
        {
            lock (_gate) return [.. _received];
        }
    }

    public static async Task<JobUpdates> ConnectAsync(TestServer server)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/events"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();
        var updates = new JobUpdates(hub);
        hub.On<JsonElement>("jobUpdated", updates.Add);
        await hub.StartAsync(TestContext.Current.CancellationToken);
        return updates;
    }

    /// <summary>The first job sent in <paramref name="state"/>; a guard of 10 s fails a test that hangs.</summary>
    public Task<JsonElement> UntilAsync(string state)
    {
        lock (_gate)
        {
            var sent = _received.Where(job => job.GetProperty("state").GetString() == state).ToList();
            if (sent.Count > 0) return Task.FromResult(sent[0]);
            var found = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaited.Add((state, found));
            return found.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private void Add(JsonElement job)
    {
        lock (_gate)
        {
            _received.Add(job);
            foreach (var awaited in _awaited.Where(awaited => awaited.State == job.GetProperty("state").GetString()).ToList())
            {
                awaited.Found.TrySetResult(job);
                _awaited.Remove(awaited);
            }
        }
    }

    public ValueTask DisposeAsync() => hub.DisposeAsync();
}
