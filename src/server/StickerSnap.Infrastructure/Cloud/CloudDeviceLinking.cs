using StickerSnap.Application.Account;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// Linking through the cloud service: a code for this computer, a long wait for the user's answer
/// in the Mini App, and the key the answer brings, kept in <see cref="DeviceCredentials"/>.
/// </summary>
internal sealed class CloudDeviceLinking(CloudClient client, DeviceCredentials device, ThisComputer computer) : IDeviceLinking
{
    public async Task<LinkInvite> InviteAsync(CancellationToken cancellationToken)
    {
        var body = new LinkRequestBody(
            computer.Name,
            computer.Os,
            await device.GetInstallationIdAsync(cancellationToken),
            computer.Platform,
            computer.AppVersion);
        var created = await client.CreateLinkAsync(body, cancellationToken);
        return new LinkInvite(created.Code, created.DeepLink, created.ExpiresAt, created.PollToken);
    }

    public async Task<LinkAnswer> WaitAsync(LinkInvite invite, TimeSpan wait, CancellationToken cancellationToken)
    {
        const string operation = "getLinkState";
        var state = await client.GetLinkStateAsync(invite.Code, invite.Secret, wait, cancellationToken);
        switch (state)
        {
            case null or { Status: LinkStatus.Expired }:
                return new LinkAnswer.Expired();
            case { Status: LinkStatus.Declined }:
                return new LinkAnswer.Declined();
            case { Status: LinkStatus.Pending }:
                return new LinkAnswer.Waiting(state.ExpiresAt ?? invite.ExpiresAt);
            case { Status: LinkStatus.Linked, DeviceKey: { Length: > 0 } key, Device: { } linked, User: { } user }:
                // The key comes only in this answer: kept before anything else can fail
                await device.SaveKeyAsync(key, cancellationToken);
                return new LinkAnswer.Linked(DeviceOf(new LinkedDeviceBody(linked, user)));
            default:
                throw CloudErrors.Garbled(operation, 200);
        }
    }

    public Task CancelAsync(LinkInvite invite, CancellationToken cancellationToken) =>
        client.CancelLinkAsync(invite.Code, invite.Secret, cancellationToken);

    public async Task<LinkedDevice?> GetAsync(CancellationToken cancellationToken)
    {
        if (await device.GetKeyAsync(cancellationToken) is not { } key) return null;

        if (await client.GetDeviceAsync(key, cancellationToken) is { } linked) return DeviceOf(linked);

        // Unlinked from the phone: the key opens nothing any more
        await device.ForgetKeyAsync(cancellationToken, only: key);
        return null;
    }

    public async Task UnlinkAsync(CancellationToken cancellationToken)
    {
        if (await device.GetKeyAsync(cancellationToken) is not { } key) return;

        await client.UnlinkDeviceAsync(key, cancellationToken);
        await device.ForgetKeyAsync(cancellationToken, only: key);
    }

    private static LinkedDevice DeviceOf(LinkedDeviceBody body) => new(
        body.Device.Id,
        body.Device.Name,
        body.Device.LinkedAt,
        body.User.ToLinkedUser(),
        body.User.BotBlocked);
}
