using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StickerSnap.Application.Packs;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure;
using StickerSnap.Infrastructure.Telegram;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>
/// test-scenarios.md, section 7: the bot's token never reaches a log. Every address of the Bot API
/// holds it, so the clients are made the way AddInfrastructure registers them, every log is
/// caught down to Trace, and failures are checked as well as answers.
/// </summary>
public sealed class BotTokenLoggingTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-bot-log-");
    private readonly BotApiStub _api = new();
    private readonly CapturingLogs _logs = new();
    private readonly ServiceProvider _services;

    public BotTokenLoggingTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StickerSnap:DataDir"] = _data.FullName,
                ["StickerSnap:BotToken"] = BotApiStub.Token,
                ["StickerSnap:BotApiUrl"] = BotApiStub.ApiUrl.ToString(),
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs))
            .AddInfrastructure(configuration);
        services.AddHttpClient<BotApiClient>().ConfigurePrimaryHttpMessageHandler(() => _api);
        // A client registered the usual way, to show the capture sees what HttpClient logs
        services.AddHttpClient("logged").ConfigurePrimaryHttpMessageHandler(() => _api);
        _services = services.BuildServiceProvider();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _services.Dispose();
        _data.Delete(recursive: true);
    }

    [Fact]
    public async Task Writes_no_token_into_the_log_of_a_call_that_succeeds()
    {
        await _services.GetRequiredService<IStickerPublisher>().DownloadAsync("CAACAgIAAxkBAAIBc2", Path.Combine(_data.FullName, "sticker.webm"), Cancel);

        _api.Requests.Should().HaveCount(2);
        _logs.Messages.Should().NotContain(message => message.Contains(BotApiStub.Token));
    }

    [Fact]
    public async Task Writes_no_token_into_a_failure_even_when_the_network_names_the_address()
    {
        var address = $"{BotApiStub.ApiUrl}bot{BotApiStub.Token}/getStickerSet";
        _api.Throw("getStickerSet", new HttpRequestException($"Connection refused ({address})"));

        var calling = () => _services.GetRequiredService<IStickerPublisher>().GetPackAsync("kot_by_StickerSnapAppBot", Cancel);

        var error = (await calling.Should().ThrowAsync<AppException>()).Which;
        error.ToString().Should().NotContain(BotApiStub.Token);
        error.Log.Should().NotContain(BotApiStub.Token).And.Contain("<bot-token>");
        _logs.Messages.Should().NotContain(message => message.Contains(BotApiStub.Token));
    }

    [Fact]
    public async Task Catches_what_an_http_client_logs_so_the_tests_above_mean_something()
    {
        using var client = _services.GetRequiredService<IHttpClientFactory>().CreateClient("logged");

        await client.GetAsync(new Uri($"{BotApiStub.ApiUrl}bot{BotApiStub.Token}/getMe"), Cancel);

        _logs.Messages.Should().Contain(message => message.Contains(BotApiStub.Token));
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _messages);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
