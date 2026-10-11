using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>The data directory: StickerSnap__DataDir, or the user's application data.</summary>
public sealed class DataOptionsTests
{
    private static DataOptions Bound(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        using var services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        return services.GetRequiredService<IOptions<DataOptions>>().Value;
    }

    [Fact]
    public void Lies_in_the_users_application_data_by_default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Bound().DataDir.Should().Be(Path.Combine(appData, "StickerSnap"));
    }

    [Fact]
    public void Is_where_StickerSnap__DataDir_says()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "stickersnap-data");

        Bound(("StickerSnap:DataDir", dataDir)).DataDir.Should().Be(dataDir);
    }
}
