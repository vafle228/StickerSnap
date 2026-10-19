using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Account;
using StickerSnap.Core.Account;
using StickerSnap.Infrastructure.Account;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Account;

/// <summary>account.json in a temporary data directory.</summary>
public sealed class JsonAccountRepositoryTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-account-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_data.FullName, JsonAccountRepository.FileName);

    public void Dispose() => _data.Delete(recursive: true);

    private JsonAccountRepository Repository() =>
        new(Options.Create(new DataOptions { DataDir = _data.FullName }), NullLogger<JsonAccountRepository>.Instance);

    [Fact]
    public async Task Has_no_account_on_a_first_launch()
    {
        (await Repository().GetAsync(Cancel)).Should().Be(AccountRecord.None);
    }

    [Fact]
    public async Task Reads_back_what_it_saved_after_a_restart()
    {
        var saved = new AccountRecord(new LinkedUser(42, "Дима", null), "DESKTOP-7F3K2Q", BotBlocked: true);

        await Repository().SaveAsync(saved, Cancel);

        (await Repository().GetAsync(Cancel)).Should().Be(saved);
    }

    [Fact]
    public async Task Writes_the_account_and_the_computer_s_name()
    {
        await Repository().SaveAsync(new AccountRecord(new LinkedUser(42, "Дима", "dima"), "Ноутбук Димы", false), Cancel);

        var json = await File.ReadAllTextAsync(FilePath, Cancel);
        json.Should().Contain("\"version\": 2").And.Contain("\"id\": 42").And.Contain("\"firstName\": \"Дима\"")
            .And.Contain("\"username\": \"dima\"").And.Contain("\"device\": \"Ноутбук Димы\"");
    }

    [Fact]
    public async Task Reads_a_file_of_linking_through_the_bot_without_the_computer()
    {
        await File.WriteAllTextAsync(FilePath, "{ \"version\": 1, \"user\": { \"id\": 42, \"firstName\": \"Дима\" }, \"lastUpdate\": 7 }", Cancel);

        (await Repository().GetAsync(Cancel)).Should().Be(new AccountRecord(new LinkedUser(42, "Дима", null), null, false));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{ \"version\": 1, \"user\": { \"id\": 0, \"firstName\": \"\" } }")]
    public async Task Has_no_account_when_the_file_holds_none(string content)
    {
        await File.WriteAllTextAsync(FilePath, content, Cancel);

        (await Repository().GetAsync(Cancel)).User.Should().BeNull();
    }
}
