using StickerSnap.Core.Packs;

namespace StickerSnap.Application.Packs;

/// <summary>
/// The packs the app knows (packs.json), by their name; Telegram stays the truth for what is in
/// them, the registry adds where each sticker came from. Names are told apart without case, as
/// Telegram does.
/// </summary>
public interface IPackRepository
{
    /// <summary>Every pack, in the order they were added.</summary>
    Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken);

    Task<Pack?> FindAsync(string name, CancellationToken cancellationToken);

    /// <summary>Adds the pack, or replaces the one of its name in its place.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task SaveAsync(Pack pack, CancellationToken cancellationToken);

    /// <summary>Forgets the pack; nothing happens for a name it does not know.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task RemoveAsync(string name, CancellationToken cancellationToken);
}
