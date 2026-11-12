using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommonFury.Textures;
using FiveFury.Textures;

namespace HikariHopper.Infrastructure;

/// <summary>System clipboard access for text and images. View models depend on this instead of a window, so they stay
/// testable. Images are exchanged as straight (unpremultiplied) RGBA8.</summary>
public interface IClipboardService
{
    /// <summary>The clipboard text, or null when it holds none.</summary>
    Task<string?> GetTextAsync();

    Task SetTextAsync(string text);

    /// <summary>Whether the clipboard currently offers an image. Cached; <see cref="Changed"/> reports updates.</summary>
    bool HasImage { get; }

    /// <summary>Raised on the UI thread after the clipboard contents change (Qt <c>QClipboard::dataChanged</c>).</summary>
    event EventHandler? Changed;

    /// <summary>The clipboard image, or null when it holds none or it cannot be decoded.</summary>
    Task<RgbaImage?> GetImageAsync();

    /// <summary>Replaces the clipboard contents with <paramref name="image"/>. Returns false when no clipboard is available.</summary>
    Task<bool> SetImageAsync(RgbaImage image);

    /// <summary>Whether the clipboard holds files, as Windows Explorer's copy puts them there.</summary>
    bool HasFiles { get; }

    /// <summary>The local paths of the files and folders on the clipboard; empty when it holds none.</summary>
    Task<IReadOnlyList<string>> GetFilesAsync();

    /// <summary>Puts files and folders on the clipboard, for a paste here or in Windows Explorer. False when it can't.</summary>
    Task<bool> SetFilesAsync(IReadOnlyList<string> paths);
}

/// <summary>The clipboard of the application's active (or main) window through Avalonia's <see cref="IClipboard"/>.</summary>
/// <remarks>Avalonia 12 reads and writes bitmaps on Windows (CF_DIB, CF_DIBV5 and the registered <c>PNG</c> format), but
/// has no change notification. Changes are detected with <c>GetClipboardSequenceNumber</c> and image availability with
/// <c>IsClipboardFormatAvailable</c>, polled on the UI thread while anything listens to <see cref="Changed"/>. Images are
/// read from the <c>PNG</c> format first, which keeps exact straight alpha, then from Avalonia's bitmap conversion.</remarks>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private const uint CfBitmap = 2, CfDib = 8, CfHDrop = 15, CfDibV5 = 17;
    private static readonly DataFormat<byte[]> PngFormat = DataFormat.CreateBytesPlatformFormat("PNG");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private EventHandler? changed;
    private DispatcherTimer? timer;
    private uint sequence;
    private bool? hasImage;

    public event EventHandler? Changed
    {
        add
        {
            changed += value;
            StartPolling();
        }
        remove
        {
            changed -= value;
            if (changed is null)
                timer?.Stop();
        }
    }

    public bool HasImage => hasImage ??= QueryHasImage();

    public async Task<string?> GetTextAsync() => Clipboard() is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task SetTextAsync(string text)
    {
        if (Clipboard() is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task<RgbaImage?> GetImageAsync()
    {
        if (Clipboard() is not { } clipboard)
            return null;
        try
        {
            if (await clipboard.TryGetValueAsync(PngFormat) is { Length: > 0 } png)
                return RgbaImage.Read(png);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException
            or COMException or InvalidOperationException)
        {
            // Fall back to the platform bitmap conversion below.
        }
        using Bitmap? bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null)
            return null;
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return RgbaImage.Read(stream.ToArray());
    }

    public async Task<bool> SetImageAsync(RgbaImage image)
    {
        if (Clipboard() is not { } clipboard)
            return false;
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            byte[] pixels = image.Pixels.ToArray();
            int rowBytes = image.Width * 4;
            for (int row = 0; row < image.Height; row++)
                Marshal.Copy(pixels, row * rowBytes, buffer.Address + row * buffer.RowBytes, rowBytes);
        }
        var item = new DataTransferItem();
        item.SetBitmap(bitmap);
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
        Refresh();
        return true;
    }

    public bool HasFiles => OperatingSystem.IsWindows() && IsClipboardFormatAvailable(CfHDrop);

    public async Task<IReadOnlyList<string>> GetFilesAsync()
    {
        try
        {
            if (Clipboard() is not { } clipboard || await clipboard.TryGetFilesAsync() is not { } items)
                return [];
            return [.. items.Select(item => item.TryGetLocalPath()).OfType<string>()];
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // Another program has the clipboard open.
            return [];
        }
    }

    public async Task<bool> SetFilesAsync(IReadOnlyList<string> paths)
    {
        if (Window() is not { Clipboard: { } clipboard } window)
            return false;
        var items = new List<IStorageItem>();
        foreach (string path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await window.StorageProvider.TryGetFolderFromPathAsync(path)
                : await window.StorageProvider.TryGetFileFromPathAsync(path);
            if (item is not null)
                items.Add(item);
        }
        if (items.Count == 0)
            return false;
        try
        {
            await clipboard.SetFilesAsync(items);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // Another program has the clipboard open.
            return false;
        }
        Refresh();
        return true;
    }

    private static Window? Window()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lifetime?.Windows.FirstOrDefault(window => window.IsActive) ?? lifetime?.MainWindow;
    }

    private static IClipboard? Clipboard() => Window()?.Clipboard;

    private void StartPolling()
    {
        // Polling needs the UI thread; view models built elsewhere (for example in tests) simply see no updates.
        if (!OperatingSystem.IsWindows() || !Dispatcher.UIThread.CheckAccess())
            return;
        if (timer is null)
        {
            sequence = GetClipboardSequenceNumber();
            timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) =>
            {
                if (GetClipboardSequenceNumber() != sequence)
                    Refresh();
            });
        }
        timer.Start();
    }

    private void Refresh()
    {
        if (OperatingSystem.IsWindows())
            sequence = GetClipboardSequenceNumber();
        hasImage = QueryHasImage();
        changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool QueryHasImage()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        uint png = RegisterClipboardFormatW("PNG");
        return IsClipboardFormatAvailable(CfDib) || IsClipboardFormatAvailable(CfDibV5) || IsClipboardFormatAvailable(CfBitmap)
            || png != 0 && IsClipboardFormatAvailable(png);
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);
}
