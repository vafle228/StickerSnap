using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>
/// The cloud service without the network: each route ("GET /v1/device") answers what a test set,
/// by default 204 with no body, and every request is kept to be looked at. It also owns a
/// temporary data directory for the computer's device.json.
/// </summary>
internal sealed class CloudServiceStub : HttpMessageHandler
{
    /// <summary>Under a path, so a test sees the client keep it.</summary>
    public static readonly Uri ServiceUrl = new("https://cloud.test/stickersnap");

    public const string Key = "dk_test-key-of-this-computer";

    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);
    private readonly List<Request> _requests = [];
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-cloud-");

    public CloudServiceStub()
    {
        Device = new DeviceCredentials(DataOptions, NullLogger<DeviceCredentials>.Instance);
    }

    public IReadOnlyList<Request> Requests => _requests;

    /// <summary>device.json of this stub's data directory.</summary>
    public DeviceCredentials Device { get; }

    public string DataDir => _data.FullName;

    public IOptions<DataOptions> DataOptions => Options.Create(new DataOptions { DataDir = _data.FullName });

    /// <summary>The one request a test made to that route.</summary>
    public Request Single(string route) => _requests.Should().ContainSingle(request => request.Route == route).Subject;

    public CloudServiceStub Answer(string route, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers[route] = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return this;
    }

    public CloudServiceStub Answer(string route, Func<HttpResponseMessage> answer)
    {
        _answers[route] = answer;
        return this;
    }

    /// <param name="parameters">The JSON of `params`, such as {"retryAfterSec":24}</param>
    public CloudServiceStub Refuse(string route, int status, string code, string? parameters = null, int? retryAfter = null) =>
        Answer(route, () =>
        {
            var extra = parameters is null ? "" : $",\"params\":{parameters}";
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(
                    $"{{\"type\":\"https://stickersnap.app/problems/{code}\",\"status\":{status},\"title\":\"Refused\",\"detail\":\"Said the service\",\"code\":\"{code}\"{extra}}}",
                    Encoding.UTF8,
                    "application/problem+json"),
            };
            if (retryAfter is { } seconds) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return response;
        });

    public CloudServiceStub Throw(string route, Exception exception)
    {
        _answers[route] = () => throw exception;
        return this;
    }

    public async Task LinkAsync(CancellationToken cancellationToken) => await Device.SaveKeyAsync(Key, cancellationToken);

    /// <summary>A client of this stub, as the Infrastructure makes one.</summary>
    public CloudClient Client(Uri? serviceUrl = null) => new(
        new HttpClient(this, disposeHandler: false) { Timeout = CloudClient.Timeout },
        Options.Create(new CloudOptions { ServiceUrl = serviceUrl ?? ServiceUrl }),
        Device);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var prefix = ServiceUrl.AbsolutePath.TrimEnd('/');
        if (path.StartsWith(prefix, StringComparison.Ordinal)) path = path[prefix.Length..];
        var route = $"{request.Method} {Uri.UnescapeDataString(path)}";

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new Request(
            route,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body));

        return _answers.TryGetValue(route, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Device.Dispose();
            _data.Delete(recursive: true);
        }
        base.Dispose(disposing);
    }

    /// <param name="Route">"POST /v1/links", the path unescaped and without the query</param>
    /// <param name="Authorization">"Bearer …", or null</param>
    public sealed record Request(string Route, Uri Address, string? Authorization, string? ContentType, string Body);
}
