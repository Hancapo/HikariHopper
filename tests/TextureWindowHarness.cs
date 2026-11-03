using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HikariHopper.Tests.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Builds a headless <see cref="TextureDictionaryWindow"/> over a real view model and drives it.</summary>
internal sealed class TextureWindowHarness
{
    public TextureWindowHarness(FakeClipboard? clipboard = null)
    {
        Clipboard = clipboard ?? new FakeClipboard();
        ViewModel = TextureTestKit.Viewer(Clipboard);
        Window = new TextureDictionaryWindow(ViewModel);
    }

    public FakeClipboard Clipboard { get; }

    public TextureViewerViewModel ViewModel { get; }

    public TextureDictionaryWindow Window { get; }

    public TextureViewport Viewport => Window.Preview.Viewport;

    /// <summary>Opens a document (which shows the window) and waits for its previews and thumbnails.</summary>
    public TextureWindowHarness Open(string name, byte[] data, string path = "")
    {
        ViewModel.OpenDictionary(name, path, data);
        Settle();
        return this;
    }

    public void Settle()
    {
        TextureTestKit.Settle(ViewModel);
        Pump();
    }

    public static void Pump()
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    public void Click(double x, double y, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseMove(new Point(x, y), modifiers);
        Window.MouseDown(new Point(x, y), button, modifiers);
        Window.MouseUp(new Point(x, y), button, modifiers);
        Pump();
    }

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public void DragEnter(double x, double y, params string[] paths)
    {
        var data = new DataTransfer();
        foreach (string path in paths)
            data.Add(DataTransferItem.CreateFile(File(path)));
        Window.DragDrop(new Point(x, y), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        Pump();
    }

    public void Drop(double x, double y, params string[] paths)
    {
        var data = new DataTransfer();
        foreach (string path in paths)
            data.Add(DataTransferItem.CreateFile(File(path)));
        Window.DragDrop(new Point(x, y), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        Window.DragDrop(new Point(x, y), RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        Pump();
    }

    private IStorageItem File(string path) =>
        Window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)).GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("The headless storage provider cannot open " + path);
}
