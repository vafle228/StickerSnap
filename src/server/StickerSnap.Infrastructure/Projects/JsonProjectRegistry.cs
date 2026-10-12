using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Projects;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Projects;

/// <summary>
/// projects.json in the data directory: the list of projects with their cards. Read once and then
/// kept in memory, written whole on every change. A file that cannot be read as the list is moved
/// aside as projects.json.bad and the list starts empty: the projects themselves are safe in their
/// .ssproj files and come back by «Открыть файл…».
/// </summary>
internal sealed class JsonProjectRegistry(IOptions<DataOptions> options, ILogger<JsonProjectRegistry> logger) : IProjectRegistry
{
    public const string FileName = "projects.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<Guid, ProjectCard>? _cards;

    private string Path => System.IO.Path.Combine(options.Value.DataDir, FileName);

    public async Task<IReadOnlyList<ProjectCard>> ListAsync(CancellationToken cancellationToken) =>
        await UnderGateAsync(cards => Task.FromResult<IReadOnlyList<ProjectCard>>([.. cards.Values]), cancellationToken);

    public async Task<ProjectCard?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await UnderGateAsync(cards => Task.FromResult(cards.GetValueOrDefault(id)), cancellationToken);

    public Task SaveAsync(ProjectCard card, CancellationToken cancellationToken) =>
        UnderGateAsync(async cards =>
        {
            if (cards.TryGetValue(card.Id, out var current) && current == card) return true;
            await WriteAsync(new Dictionary<Guid, ProjectCard>(cards) { [card.Id] = card }, cancellationToken);
            return true;
        }, cancellationToken);

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        UnderGateAsync(async cards =>
        {
            if (!cards.ContainsKey(id)) return true;
            var rest = new Dictionary<Guid, ProjectCard>(cards);
            rest.Remove(id);
            await WriteAsync(rest, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task<T> UnderGateAsync<T>(Func<Dictionary<Guid, ProjectCard>, Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cards ??= await LoadAsync(cancellationToken);
            return await action(_cards);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The list in memory changes only once the file has it, so a failed write changes nothing.</summary>
    private async Task WriteAsync(Dictionary<Guid, ProjectCard> cards, CancellationToken cancellationToken)
    {
        var stored = new Stored(Stored.CurrentVersion, [.. cards.Values.OrderBy(card => card.Id).Select(StoredCard.From)]);
        await AtomicFile.WriteAsync(Path, JsonSerializer.SerializeToUtf8Bytes(stored, Json), cancellationToken);
        _cards = cards;
    }

    private async Task<Dictionary<Guid, ProjectCard>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path;
        if (!File.Exists(path)) return [];

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken);
            if (stored is { Version: Stored.CurrentVersion })
                return stored.Projects.Select(card => card.ToCard()).DistinctBy(card => card.Id).ToDictionary(card => card.Id);
            logger.LogWarning("{Path} has version {Version}, this server knows {Known}", path, stored?.Version, Stored.CurrentVersion);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{Path} is not valid JSON of the project list", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            // Unreadable, not broken: leave the file be. The next change writes over it
            logger.LogWarning(exception, "{Path} cannot be read, the project list starts empty", path);
            return [];
        }

        SetAside(path);
        return [];
    }

    /// <summary>Keeps the broken file for whoever wants to look at it, and out of the way of the next write.</summary>
    private void SetAside(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
            logger.LogWarning("A broken project list moved to {Path}.bad, the list starts empty", path);
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "A broken project list in {Path} could not be moved aside", path);
        }
    }

    // The file's shape, apart from the domain's: it outlives renames in the code.

    private sealed record Stored(int Version, IReadOnlyList<StoredCard> Projects)
    {
        public const int CurrentVersion = 1;
    }

    private sealed record StoredCard(
        Guid Id,
        string Name,
        string Path,
        string SourcePath,
        DateTimeOffset UpdatedAt,
        int SegmentCount,
        CardPreset Preset,
        double DurationSec)
    {
        public static StoredCard From(ProjectCard card) => new(
            card.Id, card.Name, card.Path, card.SourcePath, card.UpdatedAt, card.SegmentCount, card.Preset, card.DurationSec);

        public ProjectCard ToCard() => new(Id, Name, Path, SourcePath, UpdatedAt, SegmentCount, Preset, DurationSec);
    }
}
