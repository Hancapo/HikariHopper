using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HikariHopper.Rendering;

namespace HikariHopper.Views;

/// <summary>
/// The 3D viewport in the window (after RedFury's WebGpuHost): Avalonia lays out a child window, and WebGPU draws
/// into its swap chain. A left drag orbits the camera and the wheel zooms.
/// </summary>
/// <remarks>
/// Frames are drawn on <c>WM_PAINT</c>, which Windows sends only once pending input and posted messages (Avalonia's
/// dispatcher work too) are handled; a <see cref="DisplayPacer"/> asks for one per vertical blank. Dispatcher
/// priorities can't pace frames: while mouse input is pending, Avalonia defers its work to a 10-16 ms timer, which
/// stalled frames during drags.
/// </remarks>
internal sealed partial class ViewportHost : NativeControlHost
{
    // The hosts by their window, for the window procedure, which is a static entry point.
    private static readonly Dictionary<nint, ViewportHost> Hosts = [];

    private ViewportRenderer? renderer;
    private DisplayPacer? pacer;
    private nint hwnd;
    private nint previousProcedure;
    private volatile bool chained;
    private bool drawing;
    private bool stopped;
    private bool dragging;
    private int mouseX;
    private int mouseY;
    private int surfaceWidth;
    private int surfaceHeight;
    // What is framed, the world's origin until a file says where its content is; and what is shown.
    private CommonFury.Numerics.Aabb3 framed = new(new(-5), new(5));
    private ViewportMesh? shown;

    /// <summary>The renderer, once the window exists and WebGPU has started.</summary>
    public ViewportRenderer? Renderer => renderer;

    /// <summary>Points the camera at a box in the world, from a distance that shows it whole, over a grid under it.</summary>
    public void Frame(CommonFury.Numerics.Aabb3 box)
    {
        framed = box;
        renderer?.Frame(box);
    }

    /// <summary>Shows these triangles in place of any before.</summary>
    public void Show(ViewportMesh? mesh)
    {
        shown = mesh;
        renderer?.Show(mesh);
    }

    /// <summary>WebGPU started, with the GPU it draws with.</summary>
    public event Action<string>? Started;

    /// <summary>WebGPU failed to start or to draw; the viewport stops.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Escape was pressed while the viewport had the keyboard.</summary>
    public event Action? EscapePressed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The 3D viewport needs Windows.");
        IPlatformHandle handle = base.CreateNativeControlCore(parent);
        hwnd = handle.Handle;
        stopped = false;
        Hosts[hwnd] = this;
        unsafe
        {
            previousProcedure = SetWindowLongPtrW(hwnd, -4, (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WindowProcedure);
        }
        Dispatcher.UIThread.Post(Start, DispatcherPriority.Loaded);
        return handle;
    }

    private void Start()
    {
        if (stopped || hwnd == 0)
            return;
        try
        {
            (surfaceWidth, surfaceHeight) = ClientSize();
            renderer = new ViewportRenderer(hwnd, GetModuleHandleW(null), surfaceWidth, surfaceHeight);
            renderer.Frame(framed);
            renderer.Show(shown);
            pacer = new DisplayPacer(hwnd, RequestFrame);
            Started?.Invoke(renderer.AdapterName);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
            or EntryPointNotFoundException or TimeoutException or WebGpuSharp.WebGPUException)
        {
            Fail(error);
        }
    }

    private (int Width, int Height) ClientSize()
    {
        GetClientRect(hwnd, out ClientRect rect);
        return (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    // On the pacer's thread, once per vertical blank, unless frames take longer than that and already chain.
    private void RequestFrame()
    {
        if (!stopped && !chained)
            InvalidateRect(hwnd, 0, 0);
    }

    private void Draw()
    {
        long start = Stopwatch.GetTimestamp();
        drawing = true;
        try
        {
            if (!stopped && renderer is not null && IsEffectivelyVisible)
            {
                (int width, int height) = ClientSize();
                if (width != surfaceWidth || height != surfaceHeight)
                {
                    renderer.Resize(width, height);
                    (surfaceWidth, surfaceHeight) = (width, height);
                }
                renderer.Draw();
            }
        }
        catch (InvalidOperationException error)
        {
            Fail(error);
        }
        finally
        {
            drawing = false;
        }
        // A frame slower than the display asks for the next at once rather than sleeping until a vertical blank;
        // input still comes first, as WM_PAINT waits for the message queue to drain.
        chained = !stopped && pacer is { } p && Stopwatch.GetElapsedTime(start).TotalSeconds >= p.Period;
        if (chained)
            InvalidateRect(hwnd, 0, 0);
    }

    private void Stop()
    {
        stopped = true;
        dragging = false;
        pacer?.Dispose();
        pacer = null;
        renderer?.Dispose();
        renderer = null;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // The GPU lets go of the window before it is destroyed.
        Stop();
        if (previousProcedure != 0)
            SetWindowLongPtrW(control.Handle, -4, previousProcedure);
        Hosts.Remove(control.Handle);
        previousProcedure = 0;
        hwnd = 0;
        base.DestroyNativeControlCore(control);
    }

    private void Fail(Exception error)
    {
        Stop();
        // Never unwinding through the native window procedure.
        Dispatcher.UIThread.Post(() => Failed?.Invoke(error));
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (!Hosts.TryGetValue(window, out ViewportHost? host))
            return DefWindowProcW(window, message, wParam, lParam);
        try
        {
            if (host.Handle(window, message, wParam, lParam) is { } handled)
                return handled;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentOutOfRangeException)
        {
            host.Fail(error);
        }
        return host.previousProcedure != 0
            ? CallWindowProcW(host.previousProcedure, window, message, wParam, lParam)
            : DefWindowProcW(window, message, wParam, lParam);
    }

    private const uint Paint = 0x000F;
    private const uint MouseMove = 0x0200;
    private const uint LeftDown = 0x0201;
    private const uint LeftUp = 0x0202;
    private const uint Wheel = 0x020A;
    private const uint KeyPressed = 0x0100;
    private const uint CaptureChanged = 0x0215;

    /// <summary>Handles a message, or returns null to leave it to the window's own procedure.</summary>
    private nint? Handle(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == Paint && renderer is not null && !stopped)
        {
            // Validated first, so a failing frame can't repaint forever. A paint arriving while a frame is drawn
            // (blocking waits pump messages) is left to that frame, which asks for the next when it ends.
            ValidateRect(window, 0);
            if (!drawing)
                Draw();
            return 0;
        }
        if (renderer is not { } r || stopped)
            return null;
        switch (message)
        {
            case LeftDown:
                (mouseX, mouseY) = Point(lParam);
                dragging = true;
                SetFocus(window);
                SetCapture(window);
                return 0;
            case MouseMove when dragging:
                (int x, int y) = Point(lParam);
                r.Camera.Orbit(x - mouseX, y - mouseY);
                (mouseX, mouseY) = (x, y);
                return 0;
            case LeftUp:
                dragging = false;
                ReleaseCapture();
                return 0;
            case CaptureChanged:
                dragging = false;
                return null;
            case Wheel:
                r.Camera.Zoom((short)((wParam >> 16) & 0xffff) / 120f);
                return 0;
            case KeyPressed when wParam == 0x1B:
                Dispatcher.UIThread.Post(() => EscapePressed?.Invoke());
                return 0;
        }
        return null;
    }

    // Client coordinates in physical pixels, as the swap chain has them.
    private static (int X, int Y) Point(nint lParam) => ((short)((long)lParam & 0xffff), (short)(((long)lParam >> 16) & 0xffff));

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint CallWindowProcW(nint procedure, nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(nint hwnd, out ClientRect rect);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial int InvalidateRect(nint hwnd, nint rect, int erase);

    [LibraryImport("user32.dll")]
    private static partial int ValidateRect(nint hwnd, nint rect);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseCapture();
}
