using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure.Storage;
using StickerSnap.Infrastructure.Telegram;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Telegram;

/// <summary>
/// The Bot API without the network: each method answers what a test set, by default the sample
/// in TestData/BotApi/&lt;method&gt;.json, and every request is kept to be looked at.
/// </summary>
internal sealed class BotApiStub : HttpMessageHandler
{
    public const string Token = "7000000001:AAH-test_token-of_the-bot";

    public static readonly Uri ApiUrl = new("https://bot-api.test");

    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);
    private readonly List<Request> _requests = [];

    public IReadOnlyList<Request> Requests => _requests;

    /// <summary>The one request a test made, of that method.</summary>
    public Request Single(string method) => _requests.Should().ContainSingle(request => request.Method == method).Subject;

    public BotApiStub Answer(string method, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers[method] = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return this;
    }

    public BotApiStub Refuse(string method, int errorCode, string description, int? retryAfter = null)
    {
        var parameters = retryAfter is { } seconds ? $",\"parameters\":{{\"retry_after\":{seconds}}}" : "";
        return Answer(
            method,
            $"{{\"ok\":false,\"error_code\":{errorCode},\"description\":\"{description}\"{parameters}}}",
            (HttpStatusCode)errorCode);
    }

    public BotApiStub Throw(string method, Exception exception)
    {
        _answers[method] = () => throw exception;
        return this;
    }

    /// <summary>A client of this stub, as the Infrastructure makes one, with <see cref="Token"/> configured.</summary>
    public BotApiClient Client() => new(new HttpClient(this) { Timeout = BotApiClient.Timeout }, TokenOf(), Options);

    internal static BotToken TokenOf(string? token = Token, string? dataDir = null) => new(
        Microsoft.Extensions.Options.Options.Create(new BotOptions { BotToken = token, BotApiUrl = ApiUrl }),
        Microsoft.Extensions.Options.Options.Create(new DataOptions { DataDir = dataDir ?? Path.GetTempPath() }),
        NullLogger<BotToken>.Instance);

    private static IOptions<BotOptions> Options => Microsoft.Extensions.Options.Options.Create(new BotOptions { BotApiUrl = ApiUrl });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = path.StartsWith("/file/", StringComparison.Ordinal) ? "file" : path[(path.LastIndexOf('/') + 1)..];
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new Request(method, request.RequestUri, request.Content?.Headers.ContentType?.MediaType, body));

        if (_answers.TryGetValue(method, out var answer)) return answer();
        if (TestFiles.In("BotApi").Contains($"BotApi/{method}.json"))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestFiles.ReadText($"BotApi/{method}.json"), Encoding.UTF8, "application/json") };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json") };
    }

    /// <param name="Method">getMe, sendMessage…; `file` for a download</param>
    /// <param name="Body">JSON or multipart, as sent</param>
    public sealed record Request(string Method, Uri Address, string? ContentType, string Body);
}
