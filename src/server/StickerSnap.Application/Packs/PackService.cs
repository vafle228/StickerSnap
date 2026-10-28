using StickerSnap.Application.Events;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;

namespace StickerSnap.Application.Packs;

/// <summary>
/// The registry of packs against Telegram. Reading the list never asks Telegram; a check
/// (opening a pack, «обновить», «Добавить по ссылке», a deleted sticker) reads the pack with
/// getStickerSet and writes what it found into the registry, which keeps what Telegram does not
/// know: the project and segment of each sticker. Every change is sent as packChanged.
/// </summary>
public sealed class PackService(
    IPackRepository packs,
    IStickerPublisher publisher,
    IProjectRegistry projects,
    ISourcePresence presence,
    IMediaCache cache,
    IEventPublisher events,
    TimeProvider clock) : IPackService
{
    /// <summary>How many variants of a taken name are tried: kot_divan_2 to kot_divan_9.</summary>
    public const int MaxVariant = 9;

    /// <summary>
    /// One change of the registry at a time: what Telegram said is written over the pack as it is
    /// then, not as it was before the network was asked. Never held while Telegram is asked.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken) => packs.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PackView> GetAsync(string name, CancellationToken cancellationToken) =>
        await ViewAsync(await KnownAsync(name, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task<PackView> SyncAsync(string name, CancellationToken cancellationToken)
    {
        var pack = await KnownAsync(name, cancellationToken);
        var published = await publisher.GetPackAsync(pack.Name, cancellationToken);
        var checkedPack = await UpdateAsync(pack.Name, current => current.Reconciled(published, clock.GetUtcNow()), cancellationToken);
        return await ViewAsync(checkedPack, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackView> ImportAsync(string link, CancellationToken cancellationToken)
    {
        var name = PackNames.FromLink(link) ?? throw new AppException(ErrorCode.BadRequest, $"{link} names no pack")
        {
            Fields = new Dictionary<string, string[]> { ["link"] = ["no_pack"] },
        };
        if (!PackNames.IsOurs(name))
            throw new AppException(ErrorCode.NotOurPack, $"{name} is not a pack of {Core.Telegram.TelegramLimits.BotUsername}");

        var published = await publisher.GetPackAsync(name, cancellationToken)
            ?? throw new AppException(ErrorCode.PackNotFound, $"Telegram has no pack {name}");

        Pack imported;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var known = await packs.FindAsync(published.Name, cancellationToken);
            imported = known?.Reconciled(published, now) ?? Pack.Of(published, now);
            await packs.SaveAsync(imported, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.PackChangedAsync(imported, cancellationToken);
        return await ViewAsync(imported, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackView> CopyAsync(string link, string title, CancellationToken cancellationToken)
    {
        var source = PackNames.FromLink(link) ?? throw new AppException(ErrorCode.BadRequest, $"{link} names no pack")
        {
            Fields = new Dictionary<string, string[]> { ["link"] = ["no_pack"] },
        };
        var copyTitle = title.Trim();
        if (copyTitle.Length is 0 or > Core.Telegram.TelegramLimits.PackTitleMaxLength)
            throw new AppException(ErrorCode.BadRequest, $"A pack title is 1..{Core.Telegram.TelegramLimits.PackTitleMaxLength} characters")
            {
                Fields = new Dictionary<string, string[]> { ["title"] = ["length"] },
            };

        var name = await FreeNameAsync(CopyName(copyTitle, source), cancellationToken);
        var published = await publisher.CopyPackAsync(source, PackNames.Full(name), copyTitle, cancellationToken);

        Pack copied;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            copied = Pack.Of(published, clock.GetUtcNow());
            await packs.SaveAsync(copied, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.PackChangedAsync(copied, cancellationToken);
        return await ViewAsync(copied, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pack = await KnownAsync(name, cancellationToken);
            await packs.RemoveAsync(pack.Name, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteStickerAsync(string name, string stickerId, CancellationToken cancellationToken)
    {
        var pack = await KnownAsync(name, cancellationToken);
        // Read first: the file_id to delete by is the one Telegram gives now
        var published = await publisher.GetPackAsync(pack.Name, cancellationToken);
        if (published is null)
        {
            await UpdateAsync(pack.Name, current => current.Reconciled(null, clock.GetUtcNow()), cancellationToken);
            throw new AppException(ErrorCode.PackNotFound, $"Telegram has no pack {pack.Name}");
        }

        if (published.Stickers.FirstOrDefault(sticker => sticker.UniqueId == stickerId) is { } sticker)
            await publisher.DeleteStickerAsync(pack.Name, sticker.Id, cancellationToken);

        await UpdateAsync(
            pack.Name, current => current.Reconciled(published, clock.GetUtcNow()).Without(stickerId), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaFile> StickerMediaAsync(string name, string stickerId, CancellationToken cancellationToken)
    {
        var pack = await KnownAsync(name, cancellationToken);
        var sticker = pack.Find(stickerId)
            ?? throw new AppException(ErrorCode.FileNotFound, $"{pack.Name} has no sticker {stickerId}");

        // By file_unique_id: the same file for as long as the sticker is in Telegram
        var key = MediaKeys.Of("pack-sticker", sticker.Id);
        var (extension, contentType) = sticker.Format switch
        {
            StickerFormat.Static => ("webp", "image/webp"),
            StickerFormat.Animated => ("tgs", "application/x-tgsticker"),
            _ => ("webm", "video/webm"),
        };
        var path = await cache.GetOrAddAsync(
            key, extension, (output, ct) => publisher.DownloadAsync(sticker.FileId, output, ct), cancellationToken);
        return new MediaFile(path, contentType, key);
    }

    /// <inheritdoc />
    public async Task<Pack> RecordSentAsync(
        PublishedPack published,
        string stickerId,
        IReadOnlyList<string> emoji,
        StickerOrigin origin,
        CancellationToken cancellationToken)
    {
        Pack sent;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var known = await packs.FindAsync(published.Name, cancellationToken);
            sent = Pack.Sent(known, published, stickerId, emoji, origin, clock.GetUtcNow());
            await packs.SaveAsync(sent, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.PackChangedAsync(sent, cancellationToken);
        return sent;
    }

    /// <inheritdoc />
    public async Task<PackNameCheck> CheckNameAsync(string title, string? name, CancellationToken cancellationToken)
    {
        var own = name ?? PackNames.FromTitle(title);
        var problems = PackNames.Check(own);
        if (problems.Count > 0 || !await IsTakenAsync(own, cancellationToken))
            return new PackNameCheck(own, PackNames.Full(own), problems, null);

        string? suggestion = null;
        for (var number = 2; number <= MaxVariant && suggestion is null; number++)
        {
            var variant = PackNames.Variant(own, number);
            if (!await IsTakenAsync(variant, cancellationToken)) suggestion = variant;
        }
        return new PackNameCheck(own, PackNames.Full(own), [PackNameProblem.Taken], suggestion);
    }

    /// <summary>The copy's own name from its title; a title of no Latin or Cyrillic letters takes the source's name.</summary>
    private static string CopyName(string title, string source)
    {
        var own = PackNames.FromTitle(title);
        if (PackNames.Check(own).Count == 0) return own;
        var fromSource = PackNames.FromTitle(source.EndsWith(PackNames.Suffix, StringComparison.OrdinalIgnoreCase)
            ? source[..^PackNames.Suffix.Length]
            : source);
        return PackNames.Check(fromSource).Count == 0 ? fromSource : "stickers";
    }

    /// <summary><paramref name="own"/>, or its first free variant: kot_divan_2 to kot_divan_9.</summary>
    /// <exception cref="AppException">pack_name_taken when none is free</exception>
    private async Task<string> FreeNameAsync(string own, CancellationToken cancellationToken)
    {
        if (!await IsTakenAsync(own, cancellationToken)) return own;
        for (var number = 2; number <= MaxVariant; number++)
        {
            var variant = PackNames.Variant(own, number);
            if (!await IsTakenAsync(variant, cancellationToken)) return variant;
        }
        throw new AppException(ErrorCode.PackNameTaken, $"{own} and its variants are taken");
    }

    /// <summary>On the list already, or a pack of that name in Telegram: ours, someone else's or masks.</summary>
    private async Task<bool> IsTakenAsync(string own, CancellationToken cancellationToken)
    {
        var full = PackNames.Full(own);
        return await packs.FindAsync(full, cancellationToken) is not null ||
               await publisher.IsNameTakenAsync(full, cancellationToken);
    }

    private async Task<Pack> KnownAsync(string name, CancellationToken cancellationToken) =>
        await packs.FindAsync(name, cancellationToken)
        ?? throw new AppException(ErrorCode.PackNotFound, $"The registry has no pack {name}");

    /// <summary>Writes <paramref name="change"/> of the pack as the registry has it now and sends packChanged.</summary>
    /// <exception cref="AppException">pack_not_found when it was forgotten meanwhile</exception>
    private async Task<Pack> UpdateAsync(string name, Func<Pack, Pack> change, CancellationToken cancellationToken)
    {
        Pack changed;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            changed = change(await KnownAsync(name, cancellationToken));
            await packs.SaveAsync(changed, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.PackChangedAsync(changed, cancellationToken);
        return changed;
    }

    private async Task<PackView> ViewAsync(Pack pack, CancellationToken cancellationToken)
    {
        var cards = (await projects.ListAsync(cancellationToken)).ToDictionary(card => card.Id);
        var stickers = pack.Stickers.Select(sticker =>
        {
            if (sticker.Origin is not { } origin) return new PackStickerView(sticker, null);
            var card = cards.GetValueOrDefault(origin.ProjectId);
            var status = card is not null && presence.Of(card.Path) == PresenceStatus.Ok ? PresenceStatus.Ok : PresenceStatus.Missing;
            return new PackStickerView(
                sticker, new OriginView(origin.ProjectId, card?.Name ?? origin.ProjectName, origin.SegmentId, status));
        });
        return new PackView(pack, [.. stickers]);
    }
}
