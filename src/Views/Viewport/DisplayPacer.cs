using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HikariHopper.Views;

/// <summary>
/// Calls <c>tick</c> once per vertical blank of the monitor showing a window, on a thread of its own (after RedFury's).
/// </summary>
internal sealed partial class DisplayPacer : IDisposable
{
    private readonly nint window;
    private readonly Action tick;
    private readonly Thread thread;
    private volatile bool stopped;
    private double period = 1.0 / 60;

    public DisplayPacer(nint window, Action tick)
    {
        this.window = window;
        this.tick = tick;
        thread = new Thread(Run) { IsBackground = true, Name = "HikariHopper display pacer", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>The time between vertical blanks in seconds, smoothed; 1/60 until measured.</summary>
    public double Period => Volatile.Read(ref period);

    private void Run()
    {
        using var blank = new VerticalBlank();
        long previous = 0;
        while (!stopped)
        {
            // A display that is off or being reconfigured has no vertical blank: wait a coarse period instead.
            if (blank.Wait(window))
            {
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
}
