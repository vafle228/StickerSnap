using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StickerSnap.Infrastructure.Telegram;

/// <summary>
/// The Bot API over HTTP: one POST per method to &lt;BotApiUrl&gt;/bot&lt;token&gt;/&lt;method&gt;,
/// its answer unwrapped or its refusal turned into an <see cref="Core.Errors.AppException"/>.
/// The token is part of every address, so the HttpClient that carries it is registered without
/// its request log, and nothing this class throws holds it.
/// </summary>
internal sealed class BotApiClient(HttpClient http, BotToken token, IOptions<BotOptions> options)
{
    /// <summary>Longer than the longest wait of getUpdates and an upload of a sticker.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A method whose parameters are plain values, sent as JSON.</summary>
    public Task<T> CallAsync<T>(string method, object parameters, CancellationToken cancellationToken) =>
        SendAsync<T>(method, JsonContent.Create(parameters, options: Json), cancellationToken);

    /// <summary>A method that sends files, as multipart/form-data; the form is disposed here.</summary>
    public async Task<T> CallAsync<T>(string method, MultipartFormDataContent form, CancellationToken cancellationToken)
    {
        using (form) return await SendAsync<T>(method, form, cancellationToken);
    }

    /// <summary>Writes a file that getFile pointed at into <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(string filePath, Stream destination, CancellationToken cancellationToken)
    {
        const string method = "file";
        using var request = new HttpRequestMessage(HttpMethod.Get, Address($"file/bot{token.Value}/{filePath}"));
        using var response = await SendAsync(method, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw BotApiErrors.Refused(method, (int)response.StatusCode, response.ReasonPhrase, retryAfter: null);

        try
        {
            await response.Content.CopyToAsync(destination, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw BotApiErrors.Unreachable(method, token.Redact(exception.Message));
        }
    }

    private async Task<T> SendAsync<T>(string method, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Address($"bot{token.Value}/{method}")) { Content = content };
        using var response = await SendAsync(method, request, cancellationToken);

        BotResponse<T>? answer;
        try
        {
            answer = await response.Content.ReadFromJsonAsync<BotResponse<T>>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            answer = null;
        }
        catch (HttpRequestException exception)
        {
            throw BotApiErrors.Unreachable(method, token.Redact(exception.Message));
        }

        if (answer is null) throw BotApiErrors.Garbled(method, (int)response.StatusCode);
        if (!answer.Ok)
        {
            throw BotApiErrors.Refused(
                method, answer.ErrorCode ?? (int)response.StatusCode, answer.Description, answer.Parameters?.RetryAfter);
        }
        return answer.Result is { } result ? result : throw BotApiErrors.Garbled(method, (int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(string method, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // Not wrapped: a handler may have put the address, and so the token, into the message
            throw BotApiErrors.Unreachable(method, token.Redact(exception.Message));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BotApiErrors.Unreachable(method, $"no answer in {http.Timeout.TotalSeconds:0} s");
        }
    }

    private Uri Address(string path) => new(new Uri(options.Value.BotApiUrl.ToString().TrimEnd('/') + "/"), path);
}
