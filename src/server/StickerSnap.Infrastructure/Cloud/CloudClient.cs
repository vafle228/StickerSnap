using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StickerSnap.Infrastructure.Cloud;

/// <summary>
/// The cloud service over HTTPS (src/contract/bot-service.yaml): linking from S4, publishing from
/// S5, feedback from F1. A call that acts for the user carries this computer's key as
/// `Authorization: Bearer`; the service, not the desktop, holds the bot's token. A refusal becomes
/// an <see cref="Core.Errors.AppException"/> with the desktop's code (<see cref="CloudErrors"/>).
/// </summary>
internal sealed class CloudClient(HttpClient http, IOptions<CloudOptions> options, DeviceCredentials device)
{
    /// <summary>Longer than the longest wait for a link (25 s) and an upload of a sticker.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>The longest wait the service holds a request for a link's answer.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(25);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Titles, emoji and feedback as they are: the body goes to the service, never into a page
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string WebmType = "video/webm";

    // Linking

    public async Task<LinkCreated> CreateLinkAsync(LinkRequestBody body, CancellationToken cancellationToken)
    {
        const string operation = "createLink";
        using var response = await SendAsync(operation, HttpMethod.Post, "v1/links", Body(body), bearer: null, cancellationToken);
        return await ReadAsync<LinkCreated>(operation, response!, cancellationToken);
    }

    /// <returns>Null when the service no longer knows the code: link_not_found</returns>
    public async Task<LinkState?> GetLinkStateAsync(string code, string pollToken, TimeSpan wait, CancellationToken cancellationToken)
    {
        const string operation = "getLinkState";
        var seconds = (int)Math.Ceiling(Math.Clamp(wait.TotalSeconds, 0, LongestWait.TotalSeconds));
        using var response = await SendAsync(
            operation, HttpMethod.Get, $"v1/links/{Segment(code)}?wait={seconds}", content: null, pollToken, cancellationToken,
            absentOn: HttpStatusCode.NotFound);
        return response is null ? null : await ReadAsync<LinkState>(operation, response, cancellationToken);
    }

    /// <returns>False when the service no longer knows the code</returns>
    public async Task<bool> CancelLinkAsync(string code, string pollToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            "cancelLink", HttpMethod.Delete, $"v1/links/{Segment(code)}", content: null, pollToken, cancellationToken,
            absentOn: HttpStatusCode.NotFound);
        return response is not null;
    }

    /// <returns>Null when the service no longer takes the key: it was revoked from the phone</returns>
    public async Task<LinkedDeviceBody?> GetDeviceAsync(string key, CancellationToken cancellationToken)
    {
        const string operation = "getDevice";
        using var response = await SendAsync(
            operation, HttpMethod.Get, "v1/device", content: null, key, cancellationToken, absentOn: HttpStatusCode.Unauthorized);
        return response is null ? null : await ReadAsync<LinkedDeviceBody>(operation, response, cancellationToken);
    }

    /// <summary>Revokes <paramref name="key"/>; a key revoked already is no error.</summary>
    public async Task UnlinkDeviceAsync(string key, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(
            "unlinkThisDevice", HttpMethod.Delete, "v1/device", content: null, key, cancellationToken,
            absentOn: HttpStatusCode.Unauthorized);
    }

    // Feedback

    /// <param name="key">This computer's key when it is linked; null sends the feedback without a link</param>
    /// <returns>Null when the service did not take <paramref name="key"/>: it was revoked</returns>
    public async Task<FeedbackCreated?> SendFeedbackAsync(FeedbackRequest body, string? key, CancellationToken cancellationToken)
    {
        const string operation = "sendFeedback";
        using var response = await SendAsync(
            operation, HttpMethod.Post, "v1/feedback", Body(body), key, cancellationToken,
            absentOn: key is null ? null : HttpStatusCode.Unauthorized);
        return response is null ? null : await ReadAsync<FeedbackCreated>(operation, response, cancellationToken);
    }

    // Publishing, on behalf of the user this computer is linked to

    /// <param name="webm">The sticker's file; the service takes up to 262 144 bytes</param>
    public async Task<UploadedSticker> UploadStickerAsync(Stream webm, CancellationToken cancellationToken)
    {
        const string operation = "uploadSticker";
        var content = new StreamContent(webm);
        content.Headers.ContentType = new MediaTypeHeaderValue(WebmType);
        using var response = await SendAsync(
            operation, HttpMethod.Post, "v1/stickers/upload", content, await KeyAsync(operation, cancellationToken), cancellationToken);
        return await ReadAsync<UploadedSticker>(operation, response!, cancellationToken);
    }

    public Task<CloudPack> CreatePackAsync(NewPackBody body, CancellationToken cancellationToken) =>
        PackAsync("createPack", HttpMethod.Post, "v1/packs", Body(body), cancellationToken);

    public Task<CloudPack> CopyPackAsync(CopyPackBody body, CancellationToken cancellationToken) =>
        PackAsync("copyPack", HttpMethod.Post, "v1/packs/copies", Body(body), cancellationToken);

    /// <returns>Null for a pack that is not this user's or not in the messenger: pack_not_found</returns>
    public async Task<CloudPack?> GetPackAsync(string name, CancellationToken cancellationToken)
    {
        const string operation = "getPublishedPack";
        using var response = await SendAsync(
            operation, HttpMethod.Get, $"v1/packs/{Segment(name)}", content: null, await KeyAsync(operation, cancellationToken),
            cancellationToken, absentOn: HttpStatusCode.NotFound);
        return response is null ? null : await ReadAsync<CloudPack>(operation, response, cancellationToken);
    }

    /// <summary>Whether a new pack cannot take <paramref name="name"/>, whoever made the pack of that name.</summary>
    public async Task<bool> IsPackNameTakenAsync(string name, CancellationToken cancellationToken)
    {
        const string operation = "checkPackName";
        using var response = await SendAsync(
            operation, HttpMethod.Get, $"v1/pack-names/{Segment(name)}", content: null, await KeyAsync(operation, cancellationToken),
            cancellationToken);
        return (await ReadAsync<PackNameState>(operation, response!, cancellationToken)).Taken;
    }

    public Task<CloudPack> AddStickerAsync(string pack, InputSticker sticker, CancellationToken cancellationToken) =>
        PackAsync("addSticker", HttpMethod.Post, $"v1/packs/{Segment(pack)}/stickers", Body(sticker), cancellationToken);

    public Task<CloudPack> ReplaceStickerAsync(string pack, string stickerId, InputSticker sticker, CancellationToken cancellationToken) =>
        PackAsync(
            "replaceSticker", HttpMethod.Put, $"v1/packs/{Segment(pack)}/stickers/{Segment(stickerId)}", Body(sticker), cancellationToken);

    /// <summary>The last sticker takes the pack with it.</summary>
    public async Task RemoveStickerAsync(string pack, string stickerId, CancellationToken cancellationToken)
    {
        const string operation = "removeSticker";
        using var _ = await SendAsync(
            operation, HttpMethod.Delete, $"v1/packs/{Segment(pack)}/stickers/{Segment(stickerId)}", content: null,
            await KeyAsync(operation, cancellationToken), cancellationToken);
    }

    /// <summary>Writes the sticker's .webm into <paramref name="destination"/>.</summary>
    public async Task DownloadStickerAsync(string stickerId, Stream destination, CancellationToken cancellationToken)
    {
        const string operation = "downloadSticker";
        using var response = await SendAsync(
            operation, HttpMethod.Get, $"v1/stickers/{Segment(stickerId)}/file", content: null,
            await KeyAsync(operation, cancellationToken), cancellationToken);
        try
        {
            await response!.Content.CopyToAsync(destination, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw CloudErrors.Unreachable(operation, exception.Message);
        }
    }

    private async Task<CloudPack> PackAsync(
        string operation,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(operation, method, path, content, await KeyAsync(operation, cancellationToken), cancellationToken);
        return await ReadAsync<CloudPack>(operation, response!, cancellationToken);
    }

    private async Task<string> KeyAsync(string operation, CancellationToken cancellationToken) =>
        await device.GetKeyAsync(cancellationToken) ?? throw CloudErrors.NotLinked(operation);

    /// <param name="absentOn">A status that is an answer, not a failure: the response is null then</param>
    /// <returns>A successful response, or null for <paramref name="absentOn"/></returns>
    private async Task<HttpResponseMessage?> SendAsync(
        string operation,
        HttpMethod method,
        string path,
        HttpContent? content,
        string? bearer,
        CancellationToken cancellationToken,
        HttpStatusCode? absentOn = null)
    {
        using var request = new HttpRequestMessage(method, Address(path)) { Content = content };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw CloudErrors.Unreachable(operation, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw CloudErrors.Unreachable(operation, $"no answer in {http.Timeout.TotalSeconds:0} s");
        }

        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            if (response.StatusCode == absentOn) return null;
            throw CloudErrors.Refused(
                operation, (int)response.StatusCode, await ProblemOf(response, cancellationToken), RetryAfterOf(response));
        }
    }

    private static async Task<T> ReadAsync<T>(string operation, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw CloudErrors.Garbled(operation, (int)response.StatusCode);
        }
        catch (JsonException)
        {
            throw CloudErrors.Garbled(operation, (int)response.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            throw CloudErrors.Unreachable(operation, exception.Message);
        }
    }

    /// <summary>The Problem Details of a refusal; null for an answer without them, such as a proxy's page.</summary>
    private static async Task<CloudProblem?> ProblemOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json")) return null;
        try
        {
            return await response.Content.ReadFromJsonAsync<CloudProblem>(Json, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return null;
        }
    }

    private static int? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta ? (int)Math.Ceiling(delta.TotalSeconds) : null;

    private static JsonContent Body<T>(T body) => JsonContent.Create(body, options: Json);

    private static string Segment(string value) => Uri.EscapeDataString(value);

    /// <summary>Joined as text, so a service under a path (https://host/stickersnap) keeps it.</summary>
    private Uri Address(string path) =>
        options.Value.ServiceUrl is { } service
            ? new Uri($"{service.ToString().TrimEnd('/')}/{path}")
            : throw CloudErrors.NotConfigured();
}
