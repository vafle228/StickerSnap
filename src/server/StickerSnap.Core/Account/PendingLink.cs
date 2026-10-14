namespace StickerSnap.Core.Account;

/// <summary>A link to the bot with a one-time code, waiting for the user to press Start.</summary>
/// <param name="Code">What Start sends back: `/start &lt;code&gt;`</param>
/// <param name="DeepLink">https://t.me/StickerSnapAppBot?start=&lt;code&gt;</param>
/// <param name="ExpiresAt">After it the code links nothing</param>
public sealed record PendingLink(string Code, string DeepLink, DateTimeOffset ExpiresAt);
