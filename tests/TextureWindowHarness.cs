using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HikariHopper.Tests.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>
/// Builds a headless <see cref="TextureEditor"/> over a real view model, presented as the app presents
/// it: a modal filling a host window, here one sized so the editor is exactly 1250×738. Points given to
/// the input helpers are in the editor's own coordinates.
/// </summary>
internal sealed class TextureWindowHarness
{
    /// <summary>The editor's size inside the host window.</summary>
    public const double EditorWidth = 1250, EditorHeight = 738;

    public TextureWindowHarness(FakeClipboard? clipboard = null)
    {
        Clipboard = clipboard ?? new FakeClipboard();
        ViewModel = TextureTestKit.Viewer(Clipboard);
        Backdrop = new Panel { Background = Ui.Theme.WindowBg };
        ModalOverlay.SetIsBackdrop(Backdrop, true);
        Host = new Window
        {
            Width = EditorWidth + 2 * ModalOverlay.Inset,
            Height = EditorHeight + 2 * ModalOverlay.Inset,
            Content = Backdrop,
        };
        Host.Show();
        Window = new TextureEditor(ViewModel) { Anchor = Backdrop };
    }

    public FakeClipboard Clipboard { get; }

    public TextureViewerViewModel ViewModel { get; }

    /// <summary>The window the editor is a modal in.</summary>
    public Window Host { get; }

    /// <summary>What the editor blurs.</summary>
    public Panel Backdrop { get; }

    /// <summary>The editor (named for the window it used to be).</summary>
    public TextureEditor Window { get; }

    public TextureViewport Viewport => Window.Preview.Viewport;

    /// <summary>Opens a document (which shows the editor) and waits for its previews, thumbnails and entrance.</summary>
    public TextureWindowHarness Open(string name, byte[] data, string path = "")
    {
        ViewModel.OpenDictionary(name, path, data);
        Settle();
        // The card rises into place (at once in tests); let it, so points land where the tests expect.
        TestWait.Settle();
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

    /// <summary>A point in the editor, in the host window's coordinates.</summary>
    public Point ToHost(Point point) =>
        Window.TranslatePoint(point, Host) ?? point + new Point(ModalOverlay.Inset, ModalOverlay.Inset);

    public void Click(double x, double y, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Point at = ToHost(new Point(x, y));
        Host.MouseMove(at, modifiers);
        Host.MouseDown(at, button, modifiers);
        Host.MouseUp(at, button, modifiers);
        Pump();
    }

    public void MouseMove(Point point, RawInputModifiers modifiers = RawInputModifiers.None) => Host.MouseMove(ToHost(point), modifiers);

    public void MouseDown(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Host.MouseDown(ToHost(point), button, modifiers);

    public void MouseUp(Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Host.MouseUp(ToHost(point), button, modifiers);

    public void MouseWheel(Point point, Vector delta) => Host.MouseWheel(ToHost(point), delta);

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Host.KeyPress(key, modifiers, PhysicalKey.None, null);
        Host.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public void DragEnter(double x, double y, params string[] paths) => DragEnter(x, y, [.. paths.Select(File)]);

    public void DragEnter(double x, double y, params IStorageItem[] items)
    {
        Host.DragDrop(ToHost(new Point(x, y)), RawDragEventType.DragEnter, Data(items), DragDropEffects.Copy, RawInputModifiers.None);
        Pump();
    }

    public void Drop(double x, double y, params string[] paths) => Drop(x, y, [.. paths.Select(File)]);

    public void Drop(double x, double y, params IStorageItem[] items)
    {
        DataTransfer data = Data(items);
        Point at = ToHost(new Point(x, y));
        Host.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        Host.DragDrop(at, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        Pump();
    }

    private static DataTransfer Data(IStorageItem[] items)
    {
        var data = new DataTransfer();
        foreach (IStorageItem item in items)
            data.Add(DataTransferItem.CreateFile(item));
        return data;
    }

    private IStorageItem File(string path) =>
        Host.StorageProvider.TryGetFileFromPathAsync(new Uri(path)).GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("The headless storage provider cannot open " + path);
}
