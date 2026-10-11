using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>A full disk is disk_full; any other failure to write is save_failed.</summary>
public sealed class IoErrorsTests
{
    private static readonly int DiskFull = OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28;

    [Fact]
    public void Tells_a_full_disk_by_the_os_error()
    {
        IoErrors.Of(new IOException("There is not enough space on the disk.", DiskFull), "settings.json")
            .Code.Should().Be(ErrorCode.DiskFull);
    }

    [Fact]
    public void Calls_any_other_failure_save_failed()
    {
        IoErrors.Of(new UnauthorizedAccessException("Access denied"), "settings.json").Code.Should().Be(ErrorCode.SaveFailed);
        IoErrors.Of(new IOException("Sharing violation"), "settings.json").Code.Should().Be(ErrorCode.SaveFailed);
    }
}
