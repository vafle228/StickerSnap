using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Settings;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Settings;

/// <summary>
/// settings.json in the data directory. It is read once and then kept in memory, so a reader never
/// holds the file open while a write replaces it. A file that cannot be read is moved aside as
/// settings.json.bad and the defaults take its place: broken settings must not stop the app.
/// </summary>
internal sealed class JsonSettingsRepository(
    IOptions<DataOptions> options,
    Func<Folders> defaultFolders,
    ILogger<JsonSettingsRepository> logger) : ISettingsRepository
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        WriteIndented = true,
        // A file without a theme or an accent is broken, not light and indigo
        RespectRequiredConstructorParameters = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserSettings? _settings;

    public JsonSettingsRepository(IOptions<DataOptions> options, ILogger<JsonSettingsRepository> logger)
        : this(options, DefaultFolders.Current, logger)
    {
    }

    private string Path => System.IO.Path.Combine(options.Value.DataDir, FileName);

    public async Task<UserSettings> GetAsync(CancellationToken cancellationToken)
    {
        if (_settings is not null) return _settings;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _settings ??= await LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAsync(Path, JsonSerializer.SerializeToUtf8Bytes(Stored.From(settings), Json), cancellationToken);
            _settings = settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UserSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path;
        if (!File.Exists(path)) return UserSettings.Default(defaultFolders());

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken);
            if (stored?.ToSettings() is { } settings) return settings;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{Path} is not valid JSON of the settings", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // Unreadable, not broken: leave the file be, the next save replaces it
            logger.LogWarning(exception, "{Path} cannot be read, the defaults are used instead", path);
            return UserSettings.Default(defaultFolders());
        }

        SetAside(path);
        return UserSettings.Default(defaultFolders());
    }

    /// <summary>Keeps the broken file for whoever wants to look at it, and out of the way of the next save.</summary>
    private void SetAside(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
            logger.LogWarning("Broken settings moved to {Path}.bad, the defaults are used instead", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "Broken settings in {Path} could not be moved aside", path);
        }
    }

    /// <summary>The file's shape, apart from the domain's: it outlives renames in the code.</summary>
    private sealed record Stored(int Version, Theme Theme, Accent Accent, StoredFolders? Folders, string[]? RecentEmoji = null)
    {
        private const int CurrentVersion = 1;

        public static Stored From(UserSettings settings) => new(
            CurrentVersion,
            settings.Theme,
            settings.Accent,
            new StoredFolders(settings.Folders.Projects, settings.Folders.Stickers),
            [.. settings.RecentEmoji]);

        /// <summary>Null for a file that misses a folder: the settings are whole or not at all.</summary>
        public UserSettings? ToSettings() =>
            Folders is { Projects: { Length: > 0 } projects, Stickers: { Length: > 0 } stickers }
                ? new UserSettings(Theme, Accent, new Folders(projects, stickers), RecentEmoji ?? [])
                : null;
    }

    private sealed record StoredFolders(string? Projects, string? Stickers);
}
