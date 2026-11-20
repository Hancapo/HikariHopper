using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HikariHopper.Views;

/// <summary>
/// Calls <c>tick</c> on a thread of its own at a steady rate the frames keep up with (after RedFury's, which ticked at
/// every vertical blank): every refresh of the monitor showing a window, or every second, third or fourth
/// (<see cref="FrameTook"/> picks which). Frames a little slower than a fast display's refresh (2.8 ms at 360 Hz) went
/// out unevenly, every 3 to 8 ms, and on a variable refresh display (G-SYNC, FreeSync) each was shown as it came; an
/// even rate reads as smoother than a faster uneven one. A tick is every first, second, third or fourth vertical blank,
/// so its frame is ready for the refresh it's shown at.
/// </summary>
internal sealed partial class DisplayPacer : IDisposable
{
    private readonly nint window;
    private readonly Action tick;
    private readonly Thread thread;
    private volatile bool stopped;
    private double period = 1.0 / 60;

    // Refreshes a tick (1 to MaxDivisor), the display's refresh period as Windows has it (0 when it doesn't say), and
    // the recent frames FrameTook saw: how many, how many ran past their slot, since when, and since when all were
    // short enough for a faster rate.
    private const int MaxDivisor = 4;
    private volatile int divisor = 1;
    private double refreshPeriod;
    private int windowFrames, windowLate;
    private long windowStart, fastSince;

    public DisplayPacer(nint window, Action tick)
    {
        this.window = window;
        this.tick = tick;
        thread = new Thread(Run) { IsBackground = true, Name = "HikariHopper display pacer", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>The time between ticks in seconds, smoothed; 1/60 until measured.</summary>
    public double Period => Volatile.Read(ref period);

    /// <summary>How many of the display's refreshes a tick is.</summary>
    public int Divisor => divisor;

    /// <summary>The display's refresh period in seconds, once a vertical blank has been waited for; 0 before, or when Windows doesn't say.</summary>
    public double RefreshPeriod => Volatile.Read(ref refreshPeriod);

    /// <summary>
    /// A frame took this long, from the UI thread: frames running past their slot (a few in half a second) make a tick
    /// one refresh longer at once; two seconds of frames short enough for one refresh fewer make it shorter.
    /// </summary>
    public void FrameTook(double seconds)
    {
        double refresh = Volatile.Read(ref refreshPeriod);
        if (refresh <= 0)
            return;
        long now = Stopwatch.GetTimestamp();
        int current = divisor;
        if (windowStart == 0 || Stopwatch.GetElapsedTime(windowStart, now).TotalSeconds > .5)
            (windowStart, windowFrames, windowLate) = (now, 0, 0);
        windowFrames++;
        if (seconds > current * refresh)
            windowLate++;
        if (windowLate >= Math.Max(3, windowFrames / 20) && current < MaxDivisor)
        {
            divisor = current + 1;
            (windowStart, fastSince) = (0, 0);
            return;
        }
        // A rate one refresh faster, with room to spare.
        if (current > 1 && seconds < (current - 1) * refresh * .8)
        {
            fastSince = fastSince == 0 ? now : fastSince;
            if (Stopwatch.GetElapsedTime(fastSince, now).TotalSeconds > 2)
            {
                divisor = current - 1;
                (windowStart, fastSince) = (0, 0);
            }
        }
        else
            fastSince = 0;
    }

    private void Run()
    {
        using var blank = new VerticalBlank();
        long previous = 0;
        while (!stopped)
        {
            // A display that is off or being reconfigured has no vertical blank: wait a coarse period instead.
            if (blank.Wait(window))
            {
                Volatile.Write(ref refreshPeriod, blank.RefreshPeriod);
                // Every divisor-th blank: one sooner than its slot, by more than half a refresh, isn't the tick's.
                if (previous != 0 && blank.RefreshPeriod > 0
                    && Stopwatch.GetElapsedTime(previous).TotalSeconds < blank.RefreshPeriod * (divisor - .5))
                    continue;
                long now = Stopwatch.GetTimestamp();
                double interval = previous == 0 ? 0 : Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
                // A gap from a monitor change or a stalled wait isn't the refresh period.
                if (interval is > .001 and < .1)
                    Volatile.Write(ref period, period * .9 + interval * .1);
                previous = now;
            }
            else
            {
                Thread.Sleep(8);
                previous = 0;
            }
            if (!stopped)
                tick();
        }
    }

    // A wait lasts a refresh period at most, so stopping never holds the UI for long.
    public void Dispose()
    {
        stopped = true;
        if (Thread.CurrentThread != thread)
            thread.Join(250);
    }

    private sealed unsafe class VerticalBlank : IDisposable
    {
        private nint monitor;
        private uint adapter;
        private uint source;
        private bool open;

        /// <summary>The display's refresh period in seconds, as Windows has its mode; 0 when it doesn't say.</summary>
        public double RefreshPeriod { get; private set; }

        public bool Wait(nint window)
        {
            // The nearest monitor follows the window from display to display.
            nint current = MonitorFromWindow(window, 2);
            if (!open || current != monitor)
            {
                Close();
                monitor = current;
                open = current != 0 && Open(current);
            }
            if (!open)
                return false;
            var wait = new WaitForVerticalBlankEvent { Adapter = adapter, Source = source };
            if (D3DKMTWaitForVerticalBlankEvent(&wait) == 0)
                return true;
            Close();
            return false;
        }

        private bool Open(nint display)
        {
            var info = new MonitorInfoEx { Size = sizeof(MonitorInfoEx) };
            if (GetMonitorInfoW(display, &info) == 0)
                return false;
            // Its refresh rate, from its current mode (0 and 1 mean the hardware's default).
            var mode = new DisplayMode { Size = (ushort)sizeof(DisplayMode) };
            RefreshPeriod = EnumDisplaySettingsW(info.Device, -1, &mode) != 0 && mode.Frequency > 1 ? 1.0 / mode.Frequency : 0;
            nint dc = CreateDCW(info.Device, null, null, 0);
            if (dc == 0)
                return false;
            try
            {
                var request = new OpenAdapterFromHdc { Dc = dc };
                if (D3DKMTOpenAdapterFromHdc(&request) != 0)
                    return false;
                adapter = request.Adapter;
                source = request.Source;
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        private void Close()
        {
            if (!open)
                return;
            var close = new CloseAdapter { Adapter = adapter };
            D3DKMTCloseAdapter(&close);
            open = false;
        }

        public void Dispose() => Close();
    }

    // DEVMODEW, as far as the display's frequency.
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    private struct DisplayMode
    {
        [FieldOffset(68)]
        public ushort Size;

        [FieldOffset(184)]
        public uint Frequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromHdc
    {
        public nint Dc;
        public uint Adapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint Source;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaitForVerticalBlankEvent
    {
        public uint Adapter;
        public uint Device;
        public uint Source;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MonitorInfoEx
    {
        public int Size;
        public int L0, T0, R0, B0, L1, T1, R1, B1;
        public uint Flags;
        public fixed char Device[32];
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetMonitorInfoW(nint monitor, MonitorInfoEx* info);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial nint CreateDCW(char* driver, char* device, char* output, nint mode);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int D3DKMTOpenAdapterFromHdc(OpenAdapterFromHdc* request);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int D3DKMTWaitForVerticalBlankEvent(WaitForVerticalBlankEvent* wait);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int D3DKMTCloseAdapter(CloseAdapter* close);

    [LibraryImport("user32.dll")]
    private static unsafe partial int EnumDisplaySettingsW(char* device, int mode, DisplayMode* settings);
}
