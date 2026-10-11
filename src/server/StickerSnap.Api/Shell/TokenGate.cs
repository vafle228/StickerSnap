using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace StickerSnap.Api.Shell;

/// <summary>
/// Lets into /api and /hubs only the window the shell opened: every request carries the launch's
/// token as `Authorization: Bearer`, or as `access_token` in the query where the browser loads an
/// address by itself (SignalR, &lt;video&gt;, &lt;img&gt;). The SPA page and its files stay open:
/// there is nothing secret in them.
/// </summary>
public sealed class TokenGate(RequestDelegate next, IOptions<ShellOptions> options)
{
    private readonly byte[]? _token = options.Value.Token is { Length: > 0 } token
        ? Encoding.UTF8.GetBytes(token)
        : null;

    public Task InvokeAsync(HttpContext context)
    {
        if (_token is null || !Guarded(context.Request.Path) || Carries(context.Request))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private static bool Guarded(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs");

    private bool Carries(HttpRequest request)
    {
        string? header = request.Headers.Authorization;
        if (header is not null && header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Matches(header["Bearer ".Length..]);
        return request.Query.TryGetValue("access_token", out var query) && Matches(query.ToString());
    }

    private bool Matches(string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), _token);
}
