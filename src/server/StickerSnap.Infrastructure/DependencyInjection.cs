using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StickerSnap.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The adapters behind the Application's ports: JSON-file repositories in the data directory,
    /// FFmpeg and ffprobe processes, the Telegram Bot API client. Each binds its options from
    /// <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services;
}
