using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HikariHopper.Infrastructure;

/// <summary>
/// Drags files out of the app with Windows' own drag loop and a cursor of our choosing. Avalonia's drag keeps the system
/// drag cursors, so a badge could only follow the pointer in a window of its own, a frame behind; here the badge is the
/// cursor, which Windows draws itself and moves with the mouse.
/// </summary>
/// <remarks>
/// The data object is the shell's own (<c>SHCreateDataObject</c>) for the files' item IDs, the one Explorer drags: every
/// drop target that takes files takes it. A marker format is added so the explorer's own drop area can tell its drags
/// apart. Windows only.
/// </remarks>
internal static partial class ShellFileDrag
{
    private const int DragDropSDrop = 0x00040100, DragDropSCancel = 0x00040101, DragDropSUseDefaultCursors = 0x00040102;
    private const uint DropEffectCopy = 1, MkLeftButton = 1;
    private const uint TymedHGlobal = 1, DvAspectContent = 1, GmemMoveable = 2;
    private const ushort ClipboardFileDrop = 15; // CF_HDROP

    private static readonly StrategyBasedComWrappers Com = new();
    private static readonly Guid DataObjectId = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid DropSourceId = new("00000121-0000-0000-C000-000000000046");

    /// <summary>
    /// Drags <paramref name="paths"/> (files and folders sharing one parent folder) as copies, showing
    /// <paramref name="cursor"/> while a drop is possible and <paramref name="blockedCursor"/> while it isn't. Returns false
    /// without dragging when the shell can't describe them, so the caller can fall back.
    /// </summary>
    public static bool TryDrag(IReadOnlyList<string> paths, string markerFormat, nint cursor, nint blockedCursor) =>
        WithDragObjects(paths, markerFormat, cursor, blockedCursor,
            (data, source) => DoDragDrop(data, source, DropEffectCopy, out _) >= 0);

    /// <summary>
    /// Everything a drag of <paramref name="path"/> uses short of Windows' drag loop, which waits for real input: the shell
    /// data object must offer the file and the marker, and the drop source must answer through its COM interface as the
    /// loop would call it. For the self-test of the native build, where COM interop is what can break.
    /// </summary>
    public static bool Probe(string path)
    {
        const string marker = "application/x-hikarihopper-self-test";
        return WithDragObjects([path], marker, 0, 0, (dataPointer, sourcePointer) =>
        {
            // Separate wrappers, so the calls go through the native interfaces rather than straight to the managed objects.
            var native = new StrategyBasedComWrappers();
            var data = (IShellDataObject)native.GetOrCreateObjectForComInstance(dataPointer, CreateObjectFlags.UniqueInstance);
            var source = (IDropSource)native.GetOrCreateObjectForComInstance(sourcePointer, CreateObjectFlags.UniqueInstance);
            static FormatEtc Format(ushort format) =>
                new() { Format = format, Aspect = DvAspectContent, Index = -1, Tymed = TymedHGlobal };
            return data.QueryGetData(Format(ClipboardFileDrop)) == 0
                && data.QueryGetData(Format(RegisterClipboardFormatW(marker))) == 0
                && source.QueryContinueDrag(0, MkLeftButton) == 0
                && source.QueryContinueDrag(0, 0) == DragDropSDrop
                && source.QueryContinueDrag(1, MkLeftButton) == DragDropSCancel
                && source.GiveFeedback(DropEffectCopy) == DragDropSUseDefaultCursors;
        });
    }

    /// <summary>Builds the shell data object and our drop source for <paramref name="paths"/>, hands both to <paramref name="use"/>, then releases them.</summary>
    private static bool WithDragObjects(IReadOnlyList<string> paths, string markerFormat, nint cursor, nint blockedCursor,
        Func<nint, nint, bool> use)
    {
        if (!OperatingSystem.IsWindows() || paths.Count == 0)
            return false;
        string? parent = Path.GetDirectoryName(Path.GetFullPath(paths[0]));
        if (parent is null || paths.Any(path => !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), parent, StringComparison.OrdinalIgnoreCase)))
            return false;
        nint parentId = ILCreateFromPathW(parent);
        var itemIds = new nint[paths.Count];
        nint dataPointer = 0, sourcePointer = 0;
        try
        {
            if (parentId == 0)
                return false;
            for (int index = 0; index < paths.Count; index++)
            {
                itemIds[index] = ILCreateFromPathW(Path.GetFullPath(paths[index]));
                if (itemIds[index] == 0)
                    return false;
            }
            // The data object takes item IDs relative to the parent: the last ID of each full one.
            nint[] children = [.. itemIds.Select(ILFindLastID)];
            if (SHCreateDataObject(parentId, (uint)children.Length, children, 0, DataObjectId, out dataPointer) < 0 || dataPointer == 0)
                return false;
            AddMarker(dataPointer, markerFormat);

            var source = new CursorDropSource(cursor, blockedCursor);
            nint unknown = Com.GetOrCreateComInterfaceForObject(source, CreateComInterfaceFlags.None);
            try
            {
                if (Marshal.QueryInterface(unknown, DropSourceId, out sourcePointer) < 0)
                    return false;
            }
            finally
            {
                Marshal.Release(unknown);
            }
            return use(dataPointer, sourcePointer);
        }
        finally
        {
            if (sourcePointer != 0)
                Marshal.Release(sourcePointer);
            if (dataPointer != 0)
                Marshal.Release(dataPointer);
            foreach (nint id in itemIds)
                if (id != 0)
                    ILFree(id);
            if (parentId != 0)
                ILFree(parentId);
        }
    }

    /// <summary>
    /// A cursor from top-down, non-premultiplied BGRA pixels with its hot spot at (<paramref name="hotX"/>, <paramref name="hotY"/>);
    /// 0 when Windows refuses it. Release it with <see cref="DestroyCursor"/>.
    /// </summary>
    public static nint CreateCursor(byte[] bgra, int width, int height, int hotX, int hotY)
    {
        if (!OperatingSystem.IsWindows() || bgra.Length != width * height * 4)
            return 0;
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // top-down
            Planes = 1,
            BitCount = 32,
        };
        nint color = CreateDIBSection(0, header, 0, out nint bits, 0, 0);
        if (color == 0)
            return 0;
        nint mask = CreateBitmap(width, height, 1, 1, 0);
        try
        {
            Marshal.Copy(bgra, 0, bits, bgra.Length);
            var info = new IconInfo { IsIcon = 0, HotX = (uint)hotX, HotY = (uint)hotY, Mask = mask, Color = color };
            return CreateIconIndirect(info);
        }
        finally
        {
            DeleteObject(color);
            if (mask != 0)
                DeleteObject(mask);
        }
    }

    public static void DestroyCursor(nint cursor)
    {
        if (OperatingSystem.IsWindows() && cursor != 0)
            DestroyIcon(cursor);
    }

    /// <summary>A one-byte value under <paramref name="name"/>, the format Avalonia reads as <c>DataFormat.CreateBytesPlatformFormat(name)</c>.</summary>
    private static void AddMarker(nint dataPointer, string name)
    {
        ushort format = RegisterClipboardFormatW(name);
        nint memory = GlobalAlloc(GmemMoveable, 1);
        if (format == 0 || memory == 0)
            return;
        nint bytes = GlobalLock(memory);
        Marshal.WriteByte(bytes, (byte)'1');
        GlobalUnlock(memory);
        var data = (IShellDataObject)Com.GetOrCreateObjectForComInstance(dataPointer, CreateObjectFlags.None);
        var formatEtc = new FormatEtc { Format = format, Aspect = DvAspectContent, Index = -1, Tymed = TymedHGlobal };
        var medium = new StgMedium { Tymed = TymedHGlobal, Handle = memory };
        // With release set, the data object owns the memory from here on.
        if (data.SetData(formatEtc, medium, 1) < 0)
            GlobalFree(memory);
    }

    [GeneratedComClass]
    internal sealed partial class CursorDropSource(nint cursor, nint blockedCursor) : IDropSource
    {
        public int QueryContinueDrag(int escapePressed, uint keyState) =>
            escapePressed != 0 ? DragDropSCancel : (keyState & MkLeftButton) == 0 ? DragDropSDrop : 0;

        public int GiveFeedback(uint effect)
        {
            nint shown = effect == 0 ? blockedCursor : cursor;
            if (shown == 0)
                return DragDropSUseDefaultCursors;
            SetCursor(shown);
            return 0;
        }
    }

    [GeneratedComInterface]
    [Guid("00000121-0000-0000-C000-000000000046")]
    internal partial interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int escapePressed, uint keyState);

        [PreserveSig]
        int GiveFeedback(uint effect);
    }

    [GeneratedComInterface]
    [Guid("0000010e-0000-0000-C000-000000000046")]
    internal partial interface IShellDataObject
    {
        [PreserveSig]
        int GetData(in FormatEtc format, out StgMedium medium);

        [PreserveSig]
        int GetDataHere(in FormatEtc format, ref StgMedium medium);

        [PreserveSig]
        int QueryGetData(in FormatEtc format);

        [PreserveSig]
        int GetCanonicalFormatEtc(in FormatEtc formatIn, out FormatEtc formatOut);

        [PreserveSig]
        int SetData(in FormatEtc format, in StgMedium medium, int release);

        [PreserveSig]
        int EnumFormatEtc(uint direction, out nint enumerator);

        [PreserveSig]
        int DAdvise(in FormatEtc format, uint flags, nint sink, out uint connection);

        [PreserveSig]
        int DUnadvise(uint connection);

        [PreserveSig]
        int EnumDAdvise(out nint enumerator);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FormatEtc
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StgMedium
    {
        public uint Tymed;
        public nint Handle;
        public nint ReleaseOwner;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotX;
        public uint HotY;
        public nint Mask;
        public nint Color;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, in BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, nint bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconIndirect(in IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ILCreateFromPathW(string path);

    [LibraryImport("shell32.dll")]
    private static partial nint ILFindLastID(nint id);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint id);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateDataObject(nint folder, uint count, [In] nint[] children, nint inner, in Guid interfaceId, out nint dataObject);

    [LibraryImport("ole32.dll")]
    private static partial int DoDragDrop(nint dataObject, nint dropSource, uint allowedEffects, out uint effect);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ushort RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
