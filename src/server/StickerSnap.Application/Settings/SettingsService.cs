using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Settings;

namespace StickerSnap.Application.Settings;

/// <summary>GET and PATCH /api/v1/settings: «Настройки» change at once, without «Сохранить».</summary>
public sealed class SettingsService(ISettingsRepository repository, IFolderAccess folders, IEventPublisher events) : ISettingsService
{
    /// <summary>One change at a time: two quick PATCHes must not both start from the same file.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public Task<UserSettings> GetAsync(CancellationToken cancellationToken) => repository.GetAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<UserSettings> UpdateAsync(SettingsChange change, CancellationToken cancellationToken)
    {
        if (change.IsEmpty) throw new AppException(ErrorCode.BadRequest, "The change names no field of the settings");
        Check(change);

        UserSettings updated;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await repository.GetAsync(cancellationToken);
            updated = current.With(change);
            if (updated == current) return current;

            if (updated.Folders.Projects != current.Folders.Projects)
                await folders.PrepareAsync(updated.Folders.Projects, cancellationToken);
            if (updated.Folders.Stickers != current.Folders.Stickers)
                await folders.PrepareAsync(updated.Folders.Stickers, cancellationToken);

            await repository.SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.SettingsChangedAsync(updated, cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public async Task<UserSettings> RememberEmojiAsync(IReadOnlyList<string> used, CancellationToken cancellationToken)
    {
        UserSettings updated;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await repository.GetAsync(cancellationToken);
            updated = current.WithRecentEmoji(used);
            if (updated.RecentEmoji.SequenceEqual(current.RecentEmoji)) return current;
            await repository.SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await events.SettingsChangedAsync(updated, cancellationToken);
        return updated;
    }

    /// <summary>A folder is a whole absolute path: a relative one would land wherever the server was started.</summary>
    private static void Check(SettingsChange change)
    {
        var fields = new Dictionary<string, string[]>();
        if (Problem(change.ProjectsFolder) is { } projects) fields["folders.projects"] = [projects];
        if (Problem(change.StickersFolder) is { } stickers) fields["folders.stickers"] = [stickers];
        if (fields.Count > 0)
            throw new AppException(ErrorCode.BadRequest, "A folder of the settings is not an absolute path") { Fields = fields };
    }

    private static string? Problem(string? folder) => folder switch
    {
        null => null,
        _ when string.IsNullOrWhiteSpace(folder) => "empty",
        _ when !Path.IsPathFullyQualified(folder) => "not_absolute",
        _ => null,
    };
}
