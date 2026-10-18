using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StickerSnap.Infrastructure.Cloud;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Cloud;

/// <summary>device.json in a temporary data directory, read again by a new instance as after a restart.</summary>
public sealed class DeviceCredentialsTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-device-");
    private readonly List<DeviceCredentials> _made = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_data.FullName, DeviceCredentials.FileName);

    public void Dispose()
    {
        _made.ForEach(device => device.Dispose());
        _data.Delete(recursive: true);
    }

    private DeviceCredentials Restarted()
    {
        var device = new DeviceCredentials(Options.Create(new DataOptions { DataDir = _data.FullName }), NullLogger<DeviceCredentials>.Instance);
        _made.Add(device);
        return device;
    }

    [Fact]
    public async Task Is_not_linked_on_a_first_launch_and_writes_nothing_for_it()
    {
        (await Restarted().GetKeyAsync(Cancel)).Should().BeNull();

        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_the_same_installation_id_across_restarts()
    {
        var first = await Restarted().GetInstallationIdAsync(Cancel);

        var again = await Restarted().GetInstallationIdAsync(Cancel);

        first.Should().NotBeEmpty();
        again.Should().Be(first);
    }

    [Fact]
    public async Task Keeps_the_key_across_restarts_beside_the_installation_id()
    {
        var device = Restarted();
        var installation = await device.GetInstallationIdAsync(Cancel);

        await device.SaveKeyAsync("dk_1", Cancel);

        var restarted = Restarted();
        (await restarted.GetKeyAsync(Cancel)).Should().Be("dk_1");
        (await restarted.GetInstallationIdAsync(Cancel)).Should().Be(installation);
    }

    [Fact]
    public async Task Forgets_the_key_and_keeps_the_installation()
    {
        var device = Restarted();
        var installation = await device.GetInstallationIdAsync(Cancel);
        await device.SaveKeyAsync("dk_1", Cancel);

        await device.ForgetKeyAsync(Cancel);

        var restarted = Restarted();
        (await restarted.GetKeyAsync(Cancel)).Should().BeNull();
        (await restarted.GetInstallationIdAsync(Cancel)).Should().Be(installation);
    }

    [Fact]
    public async Task Keeps_a_key_saved_by_a_new_link_while_forgetting_the_old_one()
    {
        var device = Restarted();
        await device.SaveKeyAsync("dk_new", Cancel);

        await device.ForgetKeyAsync(Cancel, only: "dk_old");

        (await device.GetKeyAsync(Cancel)).Should().Be("dk_new");
    }

    [Fact]
    public async Task Starts_as_a_new_installation_over_a_broken_file()
    {
        await File.WriteAllTextAsync(FilePath, "{ not json", Cancel);
        var device = Restarted();

        (await device.GetKeyAsync(Cancel)).Should().BeNull();
        (await device.GetInstallationIdAsync(Cancel)).Should().NotBeEmpty();
    }
}
