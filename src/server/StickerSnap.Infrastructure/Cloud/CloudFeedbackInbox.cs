using StickerSnap.Application.Feedback;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// Feedback through the cloud service, which forwards it to the team's chat. A linked computer
/// sends it with its key, so the team can answer in the messenger; one that is not, or whose key
/// was revoked from the phone, sends it with the installation's id and the contact the user gave.
/// </summary>
internal sealed class CloudFeedbackInbox(CloudClient client, DeviceCredentials device, ThisComputer computer) : IFeedbackInbox
{
    public async Task<long> SendAsync(FeedbackLetter letter, CancellationToken cancellationToken)
    {
        var installation = await device.GetInstallationIdAsync(cancellationToken);
        if (await device.GetKeyAsync(cancellationToken) is { } key
            && await client.SendFeedbackAsync(BodyOf(letter, installation, linked: true), key, cancellationToken) is { } sent)
        {
            return sent.Id;
        }

        var created = await client.SendFeedbackAsync(BodyOf(letter, installation, linked: false), key: null, cancellationToken);
        return created!.Id;
    }

    /// <param name="linked">The service answers a linked user in the messenger and takes no contact then</param>
    private FeedbackRequest BodyOf(FeedbackLetter letter, Guid installation, bool linked) => new(
        letter.Kind switch
        {
            FeedbackKind.Bug => FeedbackType.Bug,
            FeedbackKind.Idea => FeedbackType.Idea,
            _ => FeedbackType.Other,
        },
        letter.Text,
        letter.CanReply,
        !linked && letter.CanReply && letter.Contact is { Length: > 0 } contact ? contact : null,
        installation,
        computer.AppVersion,
        computer.Platform,
        letter.Error is { } error ? new FeedbackErrorBody(error.Code, error.TraceId) : null,
        letter.Diagnostics);
}
