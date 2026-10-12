using Microsoft.AspNetCore.SignalR;

namespace StickerSnap.Api.Events;

/// <summary>
/// /hubs/events: the server tells every window what changed after its answer (contract, x-events).
/// It has no methods to call: every command is REST.
/// </summary>
public sealed class EventsHub : Hub
{
    public const string Path = "/hubs/events";
}
