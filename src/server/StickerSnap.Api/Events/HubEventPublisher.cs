using Microsoft.AspNetCore.SignalR;
using StickerSnap.Api.Contracts;
using StickerSnap.Application.Events;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Settings;
using Settings = StickerSnap.Api.Contracts.Settings;

namespace StickerSnap.Api.Events;

/// <summary>
/// Sends the events to every client of <see cref="EventsHub"/> under the contract's names, with
/// the contract's payloads. A failed send is logged and dropped: the SPA rereads after a reconnect.
/// </summary>
public sealed class HubEventPublisher(IHubContext<EventsHub> hub, ILogger<HubEventPublisher> logger) : IEventPublisher
{
    public Task SettingsChangedAsync(UserSettings settings, CancellationToken cancellationToken) =>
        SendAsync("settingsChanged", Settings.From(settings), cancellationToken);

    public Task ProjectChangedAsync(ProjectListItem project, CancellationToken cancellationToken) =>
        SendAsync("projectChanged", ProjectSummary.From(project), cancellationToken);

    public Task ProjectRemovedAsync(Guid id, CancellationToken cancellationToken) =>
        SendAsync("projectRemoved", new ProjectRemovedEvent(id), cancellationToken);

    public Task JobUpdatedAsync(Core.Jobs.Job job, CancellationToken cancellationToken) =>
        SendAsync("jobUpdated", Job.From(job), cancellationToken);

    private async Task SendAsync(string name, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(name, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Event {Event} was not sent", name);
        }
    }
}
