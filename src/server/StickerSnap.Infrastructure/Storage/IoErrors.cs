using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>A failed write as the SPA knows it: disk_full when the disk is full, save_failed otherwise.</summary>
internal static class IoErrors
{
    // ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL on Windows; ENOSPC, which .NET passes as is, elsewhere
    private const int WindowsDiskFull = 0x70;
    private const int WindowsHandleDiskFull = 0x27;
    private const int UnixNoSpace = 28;

    /// <summary>What reading or writing a file can throw for reasons outside the program.</summary>
    public static bool IsIoError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    public static AppException Of(Exception exception, string what) => IsDiskFull(exception)
        ? new AppException(ErrorCode.DiskFull, $"No space left to write {what}", exception)
        : new AppException(ErrorCode.SaveFailed, $"Cannot write {what}: {exception.Message}", exception);

    internal static bool IsDiskFull(Exception exception) => exception is IOException io && (OperatingSystem.IsWindows()
        ? (io.HResult & 0xFFFF) is WindowsDiskFull or WindowsHandleDiskFull
        : io.HResult == UnixNoSpace);
}
