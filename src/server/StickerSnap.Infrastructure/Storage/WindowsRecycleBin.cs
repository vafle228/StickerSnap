using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// The Windows recycle bin, through the shell's own file operation with undo allowed: the file
/// shows in «Корзина» and «Восстановить» puts it back. No dialog is ever shown: the server has no window.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRecycleBin : ITrash
{
    private const uint Delete = 0x0003;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmMkdir = 0x0200;
    private const ushort NoErrorUi = 0x0400;

    public Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return Task.CompletedTask;

        var operation = new FileOperation
        {
            Function = Delete,
            // A list of paths, each ended by a null, the list by another one
            From = Path.GetFullPath(path) + '\0',
            Flags = (ushort)(AllowUndo | NoConfirmation | Silent | NoErrorUi | NoConfirmMkdir),
        };
        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.AnyOperationsAborted)
            throw new AppException(ErrorCode.SaveFailed, $"Cannot move {path} to the recycle bin: shell error 0x{result:X}")
            {
                Log = $"SHFileOperation returned 0x{result:X}{(operation.AnyOperationsAborted ? ", aborted" : "")}",
            };
        return Task.CompletedTask;
    }

    // SHFILEOPSTRUCTW
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

#pragma warning disable SYSLIB1054 // A struct of strings: the source-generated marshaller would need it spelled out by hand
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref FileOperation operation);
#pragma warning restore SYSLIB1054
}
