namespace StickerSnap.Core.Account;

/// <summary>The messenger's user who confirmed this computer: the packs are made on their behalf.</summary>
/// <param name="Id">The user's id in the messenger: who owns the packs</param>
/// <param name="Username">Without the @; not every user has one</param>
public sealed record LinkedUser(long Id, string FirstName, string? Username);
