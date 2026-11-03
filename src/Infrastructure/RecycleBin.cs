using System.Runtime.InteropServices;

namespace HikariHopper.Infrastructure;

/// <summary>Moves loose files and folders to the Windows Recycle Bin without prompting.</summary>
public static partial class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

    /// <summary>Replaces the shell call in tests, so they never fill the user's Recycle Bin.</summary>
    internal static Func<string, bool>? Override { get; set; }

    public static bool MoveToTrash(string path)
    {
        if (Override is not null)
            return Override(path);
        if (!OperatingSystem.IsWindows())
            return false;
        var operation = new ShFileOperation
        {
            Func = FoDelete,
            From = Path.GetFullPath(path) + "\0\0",
            Flags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi,
        };
        return SHFileOperationW(ref operation) == 0 && !operation.AnyOperationsAborted && !Path.Exists(path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public nint Hwnd;
        public uint Func;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public nint NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOperation operation);
}
