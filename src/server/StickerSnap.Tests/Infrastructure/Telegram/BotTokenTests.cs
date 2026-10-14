using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Telegram;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>Where the bot's token comes from: the configuration (user secrets), then secrets.json in the data directory.</summary>
public sealed class BotTokenTests : IDisposable
{
    private const string FileToken = "7000000001:from_the-secrets-file";

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-secrets-");

    private string SecretsPath => Path.Combine(_data.FullName, BotToken.FileName);

    public void Dispose() => _data.Delete(recursive: true);

    [Fact]
    public void Takes_the_configured_token_first()
    {
        File.WriteAllText(SecretsPath, $"{{ \"botToken\": \"{FileToken}\" }}");

        BotApiStub.TokenOf(BotApiStub.Token, _data.FullName).Value.Should().Be(BotApiStub.Token);
    }

    [Fact]
    public void Takes_the_token_from_the_secrets_file_of_the_data_directory()
    {
        File.WriteAllText(SecretsPath, $"{{\n  // From @BotFather\n  \"botToken\": \" {FileToken} \",\n}}");

        BotApiStub.TokenOf(token: null, _data.FullName).Value.Should().Be(FileToken);
    }

    [Fact]
    public void Picks_up_a_secrets_file_put_in_place_after_the_start()
    {
        var token = BotApiStub.TokenOf(token: null, _data.FullName);
        var before = () => token.Value;
        before.Should().Throw<AppException>();

        File.WriteAllText(SecretsPath, $"{{ \"botToken\": \"{FileToken}\" }}");

        token.Value.Should().Be(FileToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ \"botToken\": \"\" }")]
    [InlineData("{ \"botToken\": \"7000000001:AAH/../../getMe\" }")]
    [InlineData("{ \"botToken\": \"not a token\" }")]
    [InlineData("{ broken")]
    public void Has_no_token_without_a_file_or_with_one_that_does_not_hold_a_token(string? secrets)
    {
        if (secrets is not null) File.WriteAllText(SecretsPath, secrets);

        var reading = () => BotApiStub.TokenOf(token: null, _data.FullName).Value;

        var error = reading.Should().Throw<AppException>().Which;
        error.Code.Should().Be(ErrorCode.BotUnavailable);
        error.Message.Should().NotContain("AAH");
    }

    [Fact]
    public void Cuts_the_token_out_of_any_text()
    {
        var token = BotApiStub.TokenOf();
        _ = token.Value;

        token.Redact($"GET https://api.telegram.org/bot{BotApiStub.Token}/getMe failed")
            .Should().Be("GET https://api.telegram.org/bot<bot-token>/getMe failed");
    }
}
