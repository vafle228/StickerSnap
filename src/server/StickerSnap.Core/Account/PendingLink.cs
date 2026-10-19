namespace StickerSnap.Core.Account;

/// <summary>A link to confirm this computer in the messenger, waiting for the user's answer.</summary>
/// <param name="Code">The one-time code the link carries</param>
/// <param name="DeepLink">https://t.me/StickerSnapAppBot?startapp=link_&lt;code&gt;</param>
/// <param name="ExpiresAt">After it the code links nothing</param>
public sealed record PendingLink(string Code, string DeepLink, DateTimeOffset ExpiresAt);
