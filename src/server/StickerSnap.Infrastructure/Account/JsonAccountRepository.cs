using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Account;
using StickerSnap.Core.Account;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Account;

/// <summary>
/// account.json in the data directory: the Telegram account this computer is linked to (id, name,
/// username) and the computer's name in the user's devices; the key itself is in device.json. A
/// file that cannot be read means no account: the user links again, which is better than an app
/// that does not start.
/// </summary>
internal sealed class JsonAccountRepository(IOptions<DataOptions> options, ILogger<JsonAccountRepository> logger) : IAccountRepository
{
    public const string FileName = "account.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // The name as it is, so the file reads by eye
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string Path => System.IO.Path.Combine(options.Value.DataDir, FileName);

    public async Task<AccountRecord> GetAsync(CancellationToken cancellationToken)
    {
        var path = Path;
        if (!File.Exists(path)) return AccountRecord.None;

        try
        {
            await using var stream = File.OpenRead(path);
            var stored = await JsonSerializer.DeserializeAsync<Stored>(stream, Json, cancellationToken);
            return stored?.ToRecord() ?? AccountRecord.None;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{Path} is not valid JSON of the account, no account is linked", path);
            return AccountRecord.None;
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "{Path} cannot be read, no account is linked", path);
            return AccountRecord.None;
        }
    }

    public Task SaveAsync(AccountRecord account, CancellationToken cancellationToken) =>
        AtomicFile.WriteAsync(Path, JsonSerializer.SerializeToUtf8Bytes(Stored.From(account), Json), cancellationToken);

    /// <summary>The file's shape, apart from the domain's: it outlives renames in the code.</summary>
    /// <remarks>Version 1 (linking through the bot directly) had no device: such a file reads as not linked.</remarks>
    private sealed record Stored(int Version, StoredUser? User, string? Device, bool BotBlocked)
    {
        private const int CurrentVersion = 2;

        public static Stored From(AccountRecord account) => new(
            CurrentVersion,
            account.User is { } user ? new StoredUser(user.Id, user.FirstName, user.Username) : null,
            account.Device,
            account.BotBlocked);

        /// <summary>A user without an id or a name is no user.</summary>
        public AccountRecord ToRecord() => new(
            User is { Id: > 0, FirstName: { Length: > 0 } name } user ? new LinkedUser(user.Id, name, user.Username) : null,
            Device is { Length: > 0 } ? Device : null,
            BotBlocked);
    }

    private sealed record StoredUser(long Id, string? FirstName, string? Username);
}
