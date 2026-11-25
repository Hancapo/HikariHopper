using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HikariHopper.Rendering;

namespace HikariHopper.Views;

/// <summary>
/// The 3D viewport in the window (after RedFury's WebGpuHost): Avalonia lays out a child window, and WebGPU draws
/// into its swap chain. A left drag orbits the camera and the wheel zooms; with the free camera, a left or right drag
/// looks around, W, A, S, D, Q and E fly (Shift faster), and the wheel sets how fast.
/// </summary>
/// <remarks>
/// The free camera looks as RedFury.World's does: by the mouse's raw motion (WM_INPUT), unaffected by the pointer's
/// acceleration and the screen's edges, gathered between frames and turned once a frame, the pointer hidden and kept in
/// the view while it drags, and put back where the drag began. Frames are drawn above the streaming workers' priority,
/// which otherwise held them back while flying.
/// </remarks>
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
    // Read by the pacer's thread too.
    private volatile bool drawing;

    // Whether a vertical blank went by while a frame was drawn.
    private volatile bool blankWhileDrawing;
    private bool stopped;
    private bool dragging;
    private int mouseX;
    private int mouseY;
    // Where the left button went down, and whether it moved off it: a press that doesn't is a click, which picks.
    private int pressX;
    private int pressY;
    private bool pressMoved;
    private int[] picked = [];
    private int surfaceWidth;
    private int surfaceHeight;
    // What is framed, the world's origin until a file says where its content is; and what is shown.
    private CommonFury.Numerics.Aabb3 framed = new(new(-5), new(5));
    private ViewportMesh? shown;
    private DrawableScene? surfaces;
    private float hours = DayCycle.DefaultHours;
    private SkyPath sky = SkyPath.Game;
    private bool[]? partsShown;
    private ViewportSegment[] overlays = [];
    private ViewportVertex[] solid = [];
    private bool free;
    private long flown;

    // The free camera's look: whether a drag turns it, the raw motion gathered since the last frame, how far the drag
    // has gone (a press that goes nowhere is a click), whether raw motion arrives (not over remote desktop, whose mouse
    // is absolute: the pointer's moves turn it then), whether the pointer is hidden, and where it was pressed.
    private bool looking;
    private int lookX;
    private int lookY;
    private int lookTravel;
    private bool rawLook;
    private bool pointerHidden;
    private ScreenPoint pressedAt;

    // The pointer it showed before it hid it: the view's window has none of its own for Windows to put back.
    private nint shownCursor;

    // Raw motion counts to the camera's look in pixels: RedFury's 0.003 radians a count.
    private const float LookPerCount = .5f;

    // The longest step the camera flies in a frame, as RedFury's: a slow frame doesn't throw it forward, and a pause
    // (a hidden viewport) doesn't fly it at all.
    private const double LongestFlight = 1.0 / 30, Pause = .25;

    /// <summary>The renderer, once the window exists and WebGPU has started.</summary>
    public ViewportRenderer? Renderer => renderer;

    /// <summary>Points the camera at a box in the world, from a distance that shows it whole, over a grid under it.</summary>
    public void Frame(CommonFury.Numerics.Aabb3 box)
    {
        framed = box;
        renderer?.Frame(box);
    }

    /// <summary>Whether the sun casts shadows: the graphics setting, which every viewer draws with.</summary>
    public bool Shadows
    {
        get => Core.GraphicsSettings.Shadows;
        set => Core.GraphicsSettings.Shadows = value;
    }

    /// <summary>The graphics settings changed and the renderer draws with them; a viewer shows them on its buttons.</summary>
    public event Action? GraphicsChanged;

    // The graphics settings, to the renderer: they apply from its next frame.
    private void ApplyGraphics()
    {
        if (renderer is null)
            return;
        renderer.Antialiasing = Core.GraphicsSettings.Antialiasing;
        renderer.Bloom = Core.GraphicsSettings.Bloom;
        renderer.AmbientOcclusion = Core.GraphicsSettings.AmbientOcclusion;
        renderer.Shadows = Core.GraphicsSettings.Shadows;
        renderer.ShadowResolution = Core.GraphicsSettings.ShadowResolution;
        renderer.Anisotropy = Core.GraphicsSettings.Anisotropy;
        renderer.RenderScale = Core.GraphicsSettings.RenderScale / 100f;
        renderer.ReflectionWidth = (uint)Core.GraphicsSettings.ReflectionWidth;
    }

    private void OnGraphicsChanged()
    {
        ApplyGraphics();
        GraphicsChanged?.Invoke();
    }

    // The settings are followed while the viewer is on screen, so a closed one isn't kept alive by them; attached, it
    // catches up with any change made while it wasn't.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Core.GraphicsSettings.Changed += OnGraphicsChanged;
        OnGraphicsChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Core.GraphicsSettings.Changed -= OnGraphicsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Whether the camera flies rather than orbits.</summary>
    public bool FreeCamera
    {
        get => free;
        set
        {
            free = value;
            if (renderer is not null)
                renderer.Camera.Free = value;
        }
    }

    private bool world;
    private long lastFrame;

    /// <summary>Whether the view shows a game's world (<see cref="ViewportRenderer.WorldShown"/>).</summary>
    public bool World
    {
        get => world;
        set
        {
            world = value;
            if (renderer is not null)
                renderer.WorldShown = value;
        }
    }

    /// <summary>
    /// Before each frame is drawn, on the UI thread, with the seconds since the one before: what streams a world in
    /// does its part then.
    /// </summary>
    internal event Action<ViewportRenderer, float>? Drawing;

    /// <summary>How fast the free camera flies, in metres a second.</summary>
    public float FreeSpeed => renderer?.Camera.Speed ?? 0;

    /// <summary>The wheel changed how fast the free camera flies.</summary>
    public event Action<float>? SpeedChanged;

    /// <summary>Gives the viewport the keyboard, for the free camera's keys.</summary>
    public void FocusViewport()
    {
        if (hwnd != 0 && IsEffectivelyVisible)
            SetFocus(hwnd);
    }

    /// <summary>Draws these triangles over the drawable, its collision, in place of any before.</summary>
    internal void ShowSolid(ViewportVertex[] triangles)
    {
        solid = triangles;
        renderer?.ShowSolid(triangles);
    }

    /// <summary>Draws these lines over the drawable (its skeleton, bounds or lights), in place of any before.</summary>
    internal void ShowOverlays(ViewportSegment[] segments)
    {
        overlays = segments;
        renderer?.ShowOverlays(segments);
    }

    /// <summary>Outlines these of the drawable's parts, by index, as picked; none outlines none.</summary>
    internal void Pick(IEnumerable<int> parts)
    {
        picked = [.. parts];
        renderer?.Pick(picked);
    }

    /// <summary>The parts outlined, by index.</summary>
    internal IReadOnlyList<int> Picked => picked;

    /// <summary>Raised when the view is clicked without dragging, with the ray from the eye through the pointer, in the world.</summary>
    public event Action<System.Numerics.Vector3, System.Numerics.Vector3>? Clicked;

    /// <summary>
    /// A click that didn't drag, at a pixel of the view (as the renderer's frame has them), and whether it was the
    /// right button's: the world picks there.
    /// </summary>
    internal event Action<int, int, bool>? PixelClicked;

    /// <summary>Draws only the drawable's parts marked true, one flag per part.</summary>
    internal void ShowParts(bool[] shown)
    {
        partsShown = shown;
        renderer?.ShowParts(shown);
    }

    /// <summary>Turns the camera to a box (a part of what is shown), leaving what was framed, and the ground, as they are.</summary>
    public void Focus(CommonFury.Numerics.Aabb3 box)
    {
        renderer?.Focus(box);
        InvalidateVisual();
    }

    /// <summary>The camera's right, up and forward in the world, for the axes gizmo; a new camera's before the GPU starts.</summary>
    public (System.Numerics.Vector3 Right, System.Numerics.Vector3 Up, System.Numerics.Vector3 Forward) CameraBasis
    {
        get
        {
            Rendering.OrbitCamera camera = renderer?.Camera ?? Unstarted;
            return (camera.Right, camera.Up, camera.Forward);
        }
    }

    private static readonly Rendering.OrbitCamera Unstarted = new();

    /// <summary>Raised after a frame that turned the camera, on the UI thread.</summary>
    public event Action? CameraTurned;

    // How the camera was turned at the last frame, to tell when it turns.
    private (float Yaw, float Pitch) turned = (float.NaN, float.NaN);

    /// <summary>Looks from along a world axis, as the gizmo asks.</summary>
    public void LookFrom(System.Numerics.Vector3 axis)
    {
        if (renderer is null)
            return;
        // Framed first, as F frames it, so the view shows it whole from that side wherever the camera was.
        renderer.Frame(framed);
        renderer.Camera.LookFrom(axis);
        CameraTurned?.Invoke();
    }

    /// <summary>Orbits the camera (or turns it, flying) by a drag in pixels, as a drag on the view does.</summary>
    public void Turn(float dx, float dy)
    {
        if (renderer?.Camera is not { } camera)
            return;
        if (camera.Free)
            camera.Look(dx, dy);
        else
            camera.Orbit(dx, dy);
    }

    // Where the camera goes once the renderer starts, if somewhere was asked for before it did.
    private (System.Numerics.Vector3 Eye, float Yaw, float Pitch)? placed;

    /// <summary>Puts the camera's eye somewhere, looking along a yaw and pitch: now, or once the viewport starts.</summary>
    public void Place(System.Numerics.Vector3 eye, float yaw, float pitch)
    {
        placed = (eye, yaw, pitch);
        if (renderer is not null)
            renderer.Camera.Place(eye, yaw, pitch);
    }

    /// <summary>Where the camera's eye is and where it looks; null before the viewport starts.</summary>
    public (System.Numerics.Vector3 Eye, float Yaw, float Pitch)? CameraSpot =>
        renderer?.Camera is { } camera ? (camera.Eye, camera.Yaw, camera.Pitch) : null;

    /// <summary>Points the camera back at what was framed last.</summary>
    public void Reframe() => renderer?.Frame(framed);

    /// <summary>The time of day the light is the game's at, 0 to 24 hours.</summary>
    public float Hours => hours;

    /// <summary>Lights the scene as the game does at this time of day.</summary>
    public void SetTime(float value)
    {
        hours = value;
        renderer?.SetTime(value);
    }

    /// <summary>The sun's and moon's paths across the sky, the game's <c>time.xml</c>'s.</summary>
    public SkyPath Sky
    {
        get => sky;
        set
        {
            sky = value;
            renderer?.SetSky(value);
            InvalidateVisual();
        }
    }

    /// <summary>Shows these triangles in place of anything before; again, after the marks it shows changed.</summary>
    public void Show(ViewportMesh? mesh)
    {
        shown = mesh;
        surfaces = null;
        renderer?.Show(mesh);
        InvalidateVisual();
    }

    /// <summary>Shows a drawable's surfaces in place of anything before.</summary>
    internal void Show(DrawableScene? drawable)
    {
        surfaces = drawable;
        partsShown = null;
        picked = [];
        overlays = [];
        solid = [];
        shown = null;
        renderer?.Show(drawable);
    }

    /// <summary>
    /// Draws the drawable shown's material <paramref name="index"/> as the scene now has it, after its values were edited.
    /// </summary>
    internal void UpdateMaterial(int index)
    {
        if (surfaces is not null && index >= 0 && index < surfaces.Materials.Count)
            renderer?.UpdateMaterial(index, surfaces.Materials[index]);
    }

    /// <summary>WebGPU started, with the GPU it draws with.</summary>
    public event Action<string>? Started;

    /// <summary>WebGPU failed to start or to draw; the viewport stops.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Escape was pressed while the viewport had the keyboard.</summary>
    public event Action? EscapePressed;

    /// <summary>A letter was pressed with Ctrl, without Alt, while the viewport had the keyboard (Ctrl+Z, say), and whether with Shift.</summary>
    public event Action<char, bool>? CommandPressed;

    /// <summary>A letter or digit was pressed while the viewport had the keyboard, without Ctrl or Alt: a shortcut.</summary>
    public event Action<char>? ShortcutPressed;

    /// <summary>
    /// A key the window's shortcuts handle (F6, Alt+Left, Alt+Right) was pressed while the viewport had the keyboard, which
    /// the window never sees then: named as <see cref="WindowShortcut"/> names it.
    /// </summary>
    public event Action<string>? KeyForwarded;

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
        // Once per window: a second renderer would make a second surface for it, which loses the GPU device.
        if (stopped || hwnd == 0 || renderer is not null)
            return;
        // Not while the GPU's warm-up has the device: the renderer would wait for it here, on the UI thread, and stop the
        // whole window for as long as the warm-up takes, seconds after the app starts. Started once it's done.
        if (Rendering.GpuDevice.WarmingUp is { IsCompleted: false } warming)
        {
            warming.ContinueWith(_ => Dispatcher.UIThread.Post(Start, DispatcherPriority.Loaded), TaskScheduler.Default);
            return;
        }
        try
        {
            (surfaceWidth, surfaceHeight) = ClientSize();
            renderer = new ViewportRenderer(hwnd, GetModuleHandleW(null), surfaceWidth, surfaceHeight);
            renderer.Frame(framed);
            if (placed is { } spot)
                renderer.Camera.Place(spot.Eye, spot.Yaw, spot.Pitch);
            renderer.SetSky(sky);
            renderer.SetTime(hours);
            ApplyGraphics();
            renderer.Camera.Free = free;
            renderer.WorldShown = world;
            if (surfaces is not null)
            {
                renderer.Show(surfaces);
                if (partsShown is not null)
                    renderer.ShowParts(partsShown);
                renderer.Pick(picked);
                renderer.ShowOverlays(overlays);
                renderer.ShowSolid(solid);
            }
            else
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
        if (stopped)
            return;
        // A blank while a frame is drawn: its paint would be dropped (left to that frame), so the frame asks for the
        // next itself when it ends. Without this, a frame that started late and straddled a blank waited for the one
        // after: 30 frames a second from frames well under a refresh.
        if (drawing)
        {
            blankWhileDrawing = true;
            return;
        }
        if (!chained)
            InvalidateRect(hwnd, 0, 0);
    }

    private void Draw()
    {
        // Frames are drawn on this (UI) thread; the streaming workers filling the cores held them back 30 to 100 ms at a
        // time while flying (RedFury's finding): the frames come first.
        if (Thread.CurrentThread.Priority != ThreadPriority.AboveNormal)
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        long start = Stopwatch.GetTimestamp();
        blankWhileDrawing = false;
        drawing = true;
        bool drew = false;
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
                Fly(renderer.Camera);
                long now = Stopwatch.GetTimestamp();
                // A pause (a hidden viewport) isn't one long frame.
                float seconds = lastFrame == 0 ? 0 : (float)Math.Min(Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds, .25);
                lastFrame = now;
                Drawing?.Invoke(renderer, seconds);
                // The exposure eases over the frames' times, as the game's does.
                renderer.FrameSeconds = seconds;
                drew = renderer.Draw();
                if ((renderer.Camera.Yaw, renderer.Camera.Pitch) != turned)
                {
                    turned = (renderer.Camera.Yaw, renderer.Camera.Pitch);
                    CameraTurned?.Invoke();
                }
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
        // The pacer's rate follows how long frames take. A frame slower than a tick asks for the next at once rather
        // than sleeping until the next; input still comes first, as WM_PAINT waits for the message queue to drain.
        double took = Stopwatch.GetElapsedTime(start).TotalSeconds;
        if (drew)
            pacer?.FrameTook(took);
        chained = !stopped && pacer is { } p && (took >= p.Period || blankWhileDrawing);
        if (chained)
            InvalidateRect(hwnd, 0, 0);
    }

    /// <summary>
    /// Moves the free camera by the keys held since the last frame, while the viewport has the keyboard: the key state
    /// is the UI thread's, current here as frames are drawn once its messages are handled.
    /// </summary>
    private void Fly(OrbitCamera camera)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = flown == 0 ? 0 : Stopwatch.GetElapsedTime(flown, now).TotalSeconds;
        flown = now;
        // The look gathered since the last frame, turned once.
        if ((lookX, lookY) != (0, 0))
        {
            if (camera.Free)
                camera.Look(lookX * LookPerCount, lookY * LookPerCount);
            (lookX, lookY) = (0, 0);
        }
        float seconds = elapsed > Pause ? 0 : (float)Math.Min(elapsed, LongestFlight);
        if (!camera.Free || GetFocus() != hwnd || seconds <= 0)
            return;
        static float Axis(int plus, int minus) => (Held(plus) ? 1 : 0) - (Held(minus) ? 1 : 0);
        float forward = Axis('W', 'S'), right = Axis('D', 'A'), up = Axis('E', 'Q');
        if (forward != 0 || right != 0 || up != 0)
            camera.Fly(forward, right, up, seconds, Held(0x10) ? 4 : 1);
    }

    private static bool Held(int key) => GetKeyState(key) < 0;

    // A free camera's drag begins: raw motion from now, the pointer where it was pressed.
    private void BeginLook(nint window)
    {
        (looking, lookX, lookY, lookTravel, rawLook) = (true, 0, 0, 0, false);
        GetCursorPos(out pressedAt);
        // Foreground only; the mouse's usual messages still come.
        var device = new RawDevice { Page = 1, Usage = 2, Target = window };
        RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawDevice>());
    }

    // Motion of the drag, raw or the pointer's: the pointer hides, kept in the view, once it's gone further than a click.
    private void AddLook(nint window, int dx, int dy)
    {
        (lookX, lookY) = (lookX + dx, lookY + dy);
        lookTravel += Math.Abs(dx) + Math.Abs(dy);
        if (pressMoved || lookTravel <= 3)
            return;
        pressMoved = true;
        pointerHidden = true;
        shownCursor = SetCursor(0);
        ConfineCursor(window);
    }

    // The drag ends (or is cut short): the pointer back where it was pressed, as it was.
    private void EndLook()
    {
        if (!looking)
            return;
        looking = false;
        var device = new RawDevice { Page = 1, Usage = 2, Flags = 1 };
        RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawDevice>());
        if (pointerHidden)
        {
            pointerHidden = false;
            unsafe
            {
                ClipCursor(null);
            }
            SetCursorPos(pressedAt.X, pressedAt.Y);
            // The pointer it showed (the arrow, if it showed none), as nothing else puts it back.
            SetCursor(shownCursor != 0 ? shownCursor : LoadCursorW(0, 32512));
            shownCursor = 0;
        }
    }

    private unsafe void ConfineCursor(nint window)
    {
        if (GetClientRect(window, out ClientRect rect) == 0)
            return;
        var start = new ScreenPoint { X = rect.Left, Y = rect.Top };
        var end = new ScreenPoint { X = rect.Right, Y = rect.Bottom };
        if (ClientToScreen(window, ref start) == 0 || ClientToScreen(window, ref end) == 0)
            return;
        var screen = new ClientRect { Left = start.X, Top = start.Y, Right = end.X, Bottom = end.Y };
        ClipCursor(&screen);
    }

    // A raw mouse packet's relative motion; false for none, or an absolute one (remote desktop, tablets).
    private static bool ReadMouseDelta(nint input, out int x, out int y)
    {
        x = y = 0;
        uint size = (uint)Marshal.SizeOf<RawPacket>();
        uint read = GetRawInputData(input, 0x10000003, out RawPacket packet, ref size, (uint)Marshal.SizeOf<RawHeader>());
        if (read != size || packet.Header.Type != 0 || (packet.Mouse.Flags & 1) != 0)
            return false;
        (x, y) = (packet.Mouse.X, packet.Mouse.Y);
        return true;
    }

    private void Stop()
    {
        EndLook();
        stopped = true;
        dragging = false;
        pacer?.Dispose();
        pacer = null;
        try
        {
            renderer?.Dispose();
        }
        catch (Exception error) when (error is InvalidOperationException or WebGpuSharp.WebGPUException)
        {
            // A lost device can't let go of what it held; it goes with the device.
        }
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

    // Not started again in its window: a second surface for a window loses the GPU device, and a lost one doesn't come
    // back in this process (Direct3D 12 keeps one device per GPU, which every object WebGPUSharp never lets go holds).
    private void Fail(Exception error)
    {
        if (Rendering.GpuDevice.IsLost(error) && error.Message != Rendering.GpuDevice.LostMessage)
        {
            renderer?.Gpu.Lose(error.Message);
            error = new InvalidOperationException(Rendering.GpuDevice.LostMessage, error);
        }
        Stop();
        // Never unwinding through the native window procedure.
        Dispatcher.UIThread.Post(() => Failed?.Invoke(error));
    }

    // A click's handlers read the renderer, whose device may have been lost meanwhile: that stops the view, with why,
    // rather than the app.
    private void Clicking(Action click)
    {
        try
        {
            click();
        }
        catch (Exception error) when (error is InvalidOperationException or WebGpuSharp.WebGPUException)
        {
            if (!stopped)
                Fail(error);
        }
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
    private const uint RightDown = 0x0204;
    private const uint RightUp = 0x0205;
    private const uint Wheel = 0x020A;
    private const uint KeyPressed = 0x0100;
    private const uint SystemKeyPressed = 0x0104;
    private const uint CaptureChanged = 0x0215;
    private const uint RawInput = 0x00FF;
    private const uint SetCursorMessage = 0x0020;
    private const uint KillFocus = 0x0008;
    private const uint CancelMode = 0x001F;

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
            case RightDown when r.Camera.Free:
                (mouseX, mouseY) = Point(lParam);
                (pressX, pressY, pressMoved) = (mouseX, mouseY, false);
                dragging = true;
                SetFocus(window);
                SetCapture(window);
                if (r.Camera.Free)
                    BeginLook(window);
                return 0;
            case RawInput when looking:
                if (ReadMouseDelta(lParam, out int rawX, out int rawY))
                {
                    rawLook = true;
                    if ((rawX, rawY) != (0, 0))
                        AddLook(window, rawX, rawY);
                }
                // Raw input wants the default handling, which frees its packet.
                return DefWindowProcW(window, message, wParam, lParam);
            case SetCursorMessage when pointerHidden:
                SetCursor(0);
                return 1;
            case MouseMove when dragging:
                (int x, int y) = Point(lParam);
                if (looking)
                {
                    // Raw motion turns the camera; the pointer's only where none comes (2 counts a pixel, as before).
                    if (!rawLook)
                        AddLook(window, (x - mouseX) * 2, (y - mouseY) * 2);
                }
                else
                {
                    pressMoved |= Math.Abs(x - pressX) + Math.Abs(y - pressY) > 3;
                    r.Camera.Orbit(x - mouseX, y - mouseY);
                }
                (mouseX, mouseY) = (x, y);
                return 0;
            case LeftUp:
            case RightUp when dragging:
                if (dragging && !pressMoved)
                {
                    (int clickX, int clickY) = Point(lParam);
                    bool right = message == RightUp;
                    if (!right)
                    {
                        (System.Numerics.Vector3 from, System.Numerics.Vector3 direction) = r.Camera.Ray(clickX, clickY, surfaceWidth, surfaceHeight);
                        Dispatcher.UIThread.Post(() => Clicking(() => Clicked?.Invoke(from, direction)));
                    }
                    Dispatcher.UIThread.Post(() => Clicking(() => PixelClicked?.Invoke(clickX, clickY, right)));
                }
                dragging = false;
                EndLook();
                ReleaseCapture();
                return 0;
            case CaptureChanged:
            case KillFocus:
            case CancelMode:
                dragging = false;
                EndLook();
                return null;
            case Wheel when r.Camera.Free:
                r.Camera.Accelerate((short)((wParam >> 16) & 0xffff) / 120f);
                float speed = r.Camera.Speed;
                Dispatcher.UIThread.Post(() => SpeedChanged?.Invoke(speed));
                return 0;
            case Wheel:
                r.Camera.Zoom((short)((wParam >> 16) & 0xffff) / 120f);
                return 0;
            case KeyPressed when wParam == 0x75:
                Dispatcher.UIThread.Post(() => KeyForwarded?.Invoke("F6"));
                return 0;
            case SystemKeyPressed when (uint)wParam is 0x25 or 0x27:
                string arrow = (uint)wParam == 0x25 ? "Alt+Left" : "Alt+Right";
                Dispatcher.UIThread.Post(() => KeyForwarded?.Invoke(arrow));
                return 0;
            case KeyPressed when wParam == 0x1B:
                Dispatcher.UIThread.Post(() => EscapePressed?.Invoke());
                return 0;
            case KeyPressed when (uint)wParam is >= 0x41 and <= 0x5A && GetKeyState(0x11) < 0 && GetKeyState(0x12) >= 0:
                char command = (char)wParam;
                bool shift = GetKeyState(0x10) < 0;
                Dispatcher.UIThread.Post(() => CommandPressed?.Invoke(command, shift));
                return 0;
            case KeyPressed when (uint)wParam is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A && GetKeyState(0x11) >= 0 && GetKeyState(0x12) >= 0:
                char key = (char)wParam;
                Dispatcher.UIThread.Post(() => ShortcutPressed?.Invoke(key));
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
    private static partial short GetKeyState(int key);

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
    private static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    private static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseCapture();

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice
    {
        public ushort Page, Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader
    {
        public uint Type, Size;
        public nint Device, WParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct RawMouse
    {
        [FieldOffset(0)]
        public ushort Flags;

        [FieldOffset(12)]
        public int X;

        [FieldOffset(16)]
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawPacket
    {
        public RawHeader Header;
        public RawMouse Mouse;
    }

    [LibraryImport("user32.dll")]
    private static partial int RegisterRawInputDevices(ref RawDevice devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(nint input, uint command, out RawPacket data, ref uint size, uint headerSize);

    [LibraryImport("user32.dll")]
    private static unsafe partial int ClipCursor(ClientRect* rect);

    [LibraryImport("user32.dll")]
    private static partial int ClientToScreen(nint hwnd, ref ScreenPoint point);

    [LibraryImport("user32.dll")]
    private static partial int GetCursorPos(out ScreenPoint point);

    [LibraryImport("user32.dll")]
    private static partial int SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);
}
